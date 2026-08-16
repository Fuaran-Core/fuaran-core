module Fuaran.Core.Tests.IdlDiffTests

open Expecto
open Fuaran.Core
open Fuaran.Core.Idl
open Fuaran.Core.Tests.UiIdl

// ---------------------------------------------------------------------------
// Phase 700 — the IDL diff classifier + host-strand report.
//
// The classification rules live in `STABILITY.md` and `VOCABULARY.md` §4 and are
// mechanical in shape but hand-applied. These tests pin the mechanisation, and
// the ones worth reading are the ones where the RIGHT answer is not the obvious
// one — because those are the classifications a human gets wrong, and therefore
// the only ones the classifier earns its keep on:
//
//   - a REQUIRED field added is breaking for emitters, not additive;
//   - moving an `omitDefault` value is a WIRE change (omit-at-default is
//     wire-visible), not a defaults tidy-up;
//   - a `hostSurface` edit is NOT a wire change at all, and obliges no codec
//     host, no corpus fixture and no spec row.
//
// Every input here is built by rendering a small `Idl` through the real
// `Artifact.render`, never by hand-writing artifact JSON. That way the tests
// exercise the same bytes the committed artifact is made of, and a change to the
// artifact encoding surfaces here rather than being masked by a hand-kept copy.
//
// Naming hazard: `DiffTests.fs` beside this file is Phase 245's TREE diff and is
// unrelated.
// ---------------------------------------------------------------------------

/// Render an `Idl` the way `--emit-idl` does, so the diff reads real artifact
/// bytes.
let private art (idl: Idl) = Artifact.render idl

let private empty: Idl =
    { Kinds = []
      Unions = []
      Enums = []
      Records = []
      Defaults = []
      NodeFields = []
      Ops = [] }

let private kind tag fields : IdlKind =
    { Tag = tag
      Category = "display"
      Fields = fields }

let private f name ty opt : IdlField = { Name = name; Type = ty; Opt = opt }

/// Diff two `Idl` values through the artifact, as the CLI does.
let private diffOf (before: Idl) (after: Idl) =
    match Diff.parse (art before), Diff.parse (art after) with
    | Ok b, Ok a -> Diff.changes b a |> List.map Diff.classify
    | Error e, _
    | _, Error e -> failtestf "artifact did not read back: %s" e

let private reportOf (before: Idl) (after: Idl) =
    match Diff.run None (art before) (art after) with
    | Ok text -> text
    | Error e -> failtestf "idl-diff: %s" e

let private severities (cs: Diff.Classification list) = cs |> List.map _.Severity

/// The surfaces the consolidated obligation set names, at any strength.
let private surfaces (before: Idl) (after: Idl) =
    diffOf before after
    |> List.collect (Diff.obligations Diff.declaredRoster)
    |> List.map _.Surface
    |> List.distinct

let private hasSurfaceContaining (needle: string) (ss: string list) =
    ss |> List.exists (fun s -> s.Contains(needle: string))

// --- the fixtures the cases below vary --------------------------------------

let private oneKind =
    { empty with
        Kinds = [ kind "Heading" [ f "text" TStr Required ] ] }

[<Tests>]
let tests =
    testList
        "Phase 700 · idl-diff classifier"
        [

          // --- the degenerate case, and determinism -------------------------

          testCase "identical revisions produce no change"
          <| fun _ ->
              Expect.isEmpty (diffOf uiIdl uiIdl) "the live UI vocabulary against itself is not a change"

              Expect.stringContains
                  (reportOf uiIdl uiIdl)
                  "No change."
                  "the report says so rather than printing an empty section"

          testCase "output is byte-identical for identical inputs"
          <| fun _ ->
              let after =
                  { oneKind with
                      Kinds = oneKind.Kinds @ [ kind "Badge" [ f "label" TStr Required ] ] }

              // Two independent runs, each parsing its own copy of the bytes —
              // so a Map enumeration order leaking into the output would differ.
              let a = reportOf oneKind after
              let b = reportOf oneKind after
              Expect.equal a b "the report must be deterministic to be diffable"

          // --- the kind set --------------------------------------------------

          testCase "a kind addition is additive, and obliges the whole §11 set"
          <| fun _ ->
              let after =
                  { oneKind with
                      Kinds = oneKind.Kinds @ [ kind "Badge" [ f "label" TStr Required ] ] }

              let cs = diffOf oneKind after
              Expect.equal (severities cs) [ Diff.Additive ] "adding a kind is a minor, not a break"

              let ss = surfaces oneKind after

              // The point of the host-strand report: every obligated surface,
              // named from the roster rather than remembered.
              for needle in
                  [ "codec: fuaran-ts"
                    "codec: fuaran-py"
                    "codec: fuaran-go"
                    "codec: fuaran-rs"
                    "render arm: fuaran-swift"
                    "render arm: fuaran-kt"
                    "veneer: C#"
                    "veneer: VB"
                    "analyzer: VB"
                    "manifest: manifest.kinds"
                    "corpus:"
                    "schema:"
                    "artifact: idl.json"
                    "WIRE_FORMAT.md §3.2" ] do
                  Expect.isTrue (hasSurfaceContaining needle ss) (sprintf "a new kind obliges '%s'" needle)

          testCase "a kind removal is a breaking wire event"
          <| fun _ ->
              let cs = diffOf oneKind empty

              Expect.contains
                  (severities cs)
                  Diff.BreakingWire
                  "retiring a $type discriminator is a /v2/ major (VOCABULARY.md §4.2)"

              Expect.stringContains (reportOf oneKind empty) "/v2/` MAJOR" "and the profile recommendation says so"

          testCase "a rename is INFERRED beside the add and remove, never instead of them"
          <| fun _ ->
              let after =
                  { empty with
                      Kinds = [ kind "Title" [ f "text" TStr Required ] ] }

              let cs = diffOf oneKind after
              let kinds = cs |> List.map _.Change

              Expect.contains kinds (Diff.KindRenamed("Heading", "Title")) "the identical signature pairs uniquely"
              Expect.contains kinds (Diff.KindRemoved "Heading") "and the removal still stands on its own"
              Expect.contains kinds (Diff.KindAdded "Title") "as does the addition"

          testCase "field-less kinds do not pair as renames"
          <| fun _ ->
              // Every field-less kind has the same empty signature, so pairing
              // them would be a coincidence dressed up as intent.
              let before = { empty with Kinds = [ kind "A" [] ] }
              let after = { empty with Kinds = [ kind "B" [] ] }

              let renames =
                  diffOf before after
                  |> List.map _.Change
                  |> List.filter (function
                      | Diff.KindRenamed _ -> true
                      | _ -> false)

              Expect.isEmpty renames "an empty signature matches everything, so it must match nothing"

          testCase "an ambiguous signature does not pair as a rename"
          <| fun _ ->
              let sig' = [ f "text" TStr Required ]

              let before =
                  { empty with
                      Kinds = [ kind "A" sig'; kind "B" sig' ] }

              let after =
                  { empty with
                      Kinds = [ kind "C" sig'; kind "D" sig' ] }

              let renames =
                  diffOf before after
                  |> List.map _.Change
                  |> List.filter (function
                      | Diff.KindRenamed _ -> true
                      | _ -> false)

              Expect.isEmpty renames "two candidates on each side is not a rename, it is a guess"

          // --- the classification a human gets wrong -------------------------

          testCase "a REQUIRED field added is breaking for emitters, not additive"
          <| fun _ ->
              let after =
                  { empty with
                      Kinds = [ kind "Heading" [ f "text" TStr Required; f "level" TInt Required ] ] }

              let cs = diffOf oneKind after
              Expect.equal (severities cs) [ Diff.BreakingForEmitters ] "the 0.2.0 / orchestration-0.1.3 lesson"

              let report = reportOf oneKind after
              Expect.stringContains report "stability_impact: breaking" "so the draft front-matter says breaking"

              Expect.stringContains
                  report
                  "downstream emitters"
                  "and the obligation set names the coordination the minor bump would hide"

          testCase "an optional field added is additive"
          <| fun _ ->
              let after =
                  { empty with
                      Kinds = [ kind "Heading" [ f "text" TStr Required; f "level" TInt Optional ] ] }

              Expect.equal
                  (severities (diffOf oneKind after))
                  [ Diff.Additive ]
                  "omitted when absent, so nothing breaks"

              Expect.stringContains
                  (reportOf oneKind after)
                  "stability_impact: additive"
                  "and the draft front-matter agrees"

          testCase "moving an omitDefault VALUE is a wire change, not a defaults tidy-up"
          <| fun _ ->
              let withDefault d =
                  { empty with
                      Kinds = [ kind "Heading" [ f "level" TInt (OmitDefault(VInt d)) ] ] }

              let cs = diffOf (withDefault 1) (withDefault 2)

              Expect.equal
                  (severities cs)
                  [ Diff.BreakingWire ]
                  "omit-at-default is wire-visible: every document sitting on the old default changes bytes"

              Expect.stringContains
                  (cs |> List.head |> _.Rationale)
                  "WIRE-VISIBLE"
                  "and the rationale says why, since this is the row most likely to be waved through"

          testCase "required -> optional is still a wire event"
          <| fun _ ->
              let after =
                  { empty with
                      Kinds = [ kind "Heading" [ f "text" TStr Optional ] ] }

              Expect.equal
                  (severities (diffOf oneKind after))
                  [ Diff.BreakingWire ]
                  "old documents stay valid, but a consumer that relied on presence now faces absence"

          // --- host surface is not wire ---------------------------------------

          testCase "a hostSurface-only edit is not a wire change and obliges no codec host"
          <| fun _ ->
              let fn sg =
                  { empty with
                      Kinds =
                          [ kind
                                "Button"
                                [ f
                                      "onClick"
                                      (TFn
                                          { FSharp = sg
                                            TypeScript = "() => Msg"
                                            Placeholder = "Unchecked.defaultof<_>" })
                                      Required ] ] }

              let before, after = fn "unit -> 'Msg", fn "int -> 'Msg"
              let cs = diffOf before after

              Expect.equal
                  (severities cs)
                  [ Diff.HostSurfaceOnly ]
                  "the generated declaration moved; the wire form is the same `<closure>` sentinel"

              let ss = surfaces before after
              Expect.equal (List.length ss) 1 "exactly one obligation — the reference host's own regeneration"

              Expect.isFalse
                  (hasSurfaceContaining "codec: fuaran-ts" ss)
                  "no third-party codec can observe this, so none is obliged"

              Expect.isFalse (hasSurfaceContaining "corpus:" ss) "and no fixture changes"

              Expect.stringContains
                  (reportOf before after)
                  "no wire-profile movement"
                  "the profile recommendation must not invent a bump"

          // The case the retroactive validation added — see
          // docs/idl-diff-retroactive-validation.md. The classifier originally
          // called Phase 707's `liveRegion` re-model a breaking wire change; it
          // was not, and the artifact contains nothing that could have told it
          // either way. Saying so is the correct verdict.
          testCase "a type change across an ERASED slot is undecided, not breaking"
          <| fun _ ->
              let hosted =
                  { empty with
                      Records =
                          [ { Name = "Accessibility"
                              Fields =
                                [ f
                                      "liveRegion"
                                      (THosted
                                          { FSharp = "HostPrelude.LiveRegionKind"
                                            Encode = "encLiveRegionKind"
                                            Decode = "decLiveRegionKind" })
                                      Optional ] } ] }

              let declared =
                  { empty with
                      Enums = [ Declare.enumWith "LiveRegionKind" [ "Polite", "polite" ] ]
                      Records =
                          [ { Name = "Accessibility"
                              Fields = [ f "liveRegion" (TEnum "LiveRegionKind") Optional ] } ] }

              let cs = diffOf hosted declared

              let typeChange =
                  cs
                  |> List.find (fun c ->
                      match c.Change with
                      | Diff.FieldTypeChanged _ -> true
                      | _ -> false)

              Expect.equal
                  typeChange.Severity
                  Diff.Unclassifiable
                  "the artifact does not state what a hosted slot admits, so it cannot say whether the sets differ"

              Expect.stringContains typeChange.Rationale "CHECK:" "and it must name the check that would settle it"

              let report = reportOf hosted declared
              Expect.stringContains report "UNDECIDED" "the verdict is undecided, not a bump recommendation"

              Expect.isFalse
                  (report.Contains "`/v2/` MAJOR — the schema")
                  "an undecided change must not be reported as a settled major"

          testCase "a type change between two DESCRIBED types is still breaking"
          <| fun _ ->
              // The escape hatch above must not swallow the ordinary case.
              let ty t =
                  { empty with
                      Kinds = [ kind "Heading" [ f "level" t Required ] ] }

              Expect.equal
                  (severities (diffOf (ty TInt) (ty TStr)))
                  [ Diff.BreakingWire ]
                  "int -> str is fully described by the artifact and decodes differently"

          testCase "a category re-classification is metadata, never serialised"
          <| fun _ ->
              let after =
                  { empty with
                      Kinds =
                          [ { kind "Heading" [ f "text" TStr Required ] with
                                Category = "layout" } ] }

              Expect.equal (severities (diffOf oneKind after)) [ Diff.HostSurfaceOnly ] "Category is IDL metadata"

          // --- the other discriminator families --------------------------------

          testCase "a union case addition carries the identical §11 wire cost"
          <| fun _ ->
              let union cases =
                  { empty with
                      Unions =
                          [ { Name = "Binding"
                              Params = [ "T" ]
                              Cases = cases } ] }

              let stat: IdlUnionCase =
                  { Tag = "Static"
                    Fields = [ f "value" TStr Required ] }

              let state: IdlUnionCase =
                  { Tag = "State"
                    Fields = [ f "key" TStr Required ] }

              let before, after = union [ stat ], union [ stat; state ]
              let cs = diffOf before after

              Expect.contains (severities cs) Diff.Additive "additive on the wire"

              Expect.stringContains
                  (cs
                   |> List.find (fun c -> c.Change = Diff.UnionCaseAdded("Binding", "State"))
                   |> _.Rationale)
                  "IDENTICAL"
                  "the quiet-churn caveat: cheaper on confusion, EQUAL on wire coupling"

              let ss = surfaces before after
              Expect.isTrue (hasSurfaceContaining "codec: fuaran-rs" ss) "every codec host is still obliged"

              Expect.isFalse
                  (hasSurfaceContaining "veneer: C#" ss)
                  "but the veneers pin NodeKind, so they are a CHECK row rather than a hard obligation"

          testCase "an enum case addition cites the host-lag commitment"
          <| fun _ ->
              let e cases =
                  { empty with
                      Enums = [ Declare.enumOf "Tone" cases ] }

              let before, after = e [ "Neutral" ], e [ "Neutral"; "Positive" ]
              let cs = diffOf before after

              Expect.equal (severities cs) [ Diff.Additive ] "a wider closed set admits every old document"

              Expect.stringContains
                  (cs |> List.head |> _.Rationale)
                  "UNKNOWN_DU_CASE"
                  "a decoder that predates the case REJECTS it — §4.3 is the reason the growth rate matters"

          testCase "a wire-string remap is a wire change; a host-case remap is not"
          <| fun _ ->
              let identity =
                  { empty with
                      Enums = [ Declare.enumOf "Live" [ "Polite" ] ] }

              let remappedWire =
                  { empty with
                      Enums = [ Declare.enumWith "Live" [ "Polite", "polite" ] ] }

              let remappedHost =
                  { empty with
                      Enums = [ Declare.enumWith "Live" [ "Courteous", "polite" ] ] }

              // Identity -> lower-case wire string: the admitted set moved.
              Expect.contains
                  (severities (diffOf identity remappedWire))
                  Diff.BreakingWire
                  "\"Polite\" is no longer admitted and \"polite\" newly is"

              // Same wire strings, different F# case names: hostSurface only.
              Expect.equal
                  (severities (diffOf remappedWire remappedHost))
                  [ Diff.HostSurfaceOnly ]
                  "hostCases is a §13 hostSurface key — a source-compat event, not a wire one"

          testCase "an op addition is additive; an op removal is worse than a kind removal"
          <| fun _ ->
              let ops os = { empty with Ops = os }

              let insert =
                  { Tag = "InsertChild"
                    Category = "op"
                    Fields = [ f "child" TNode Required ] }

              let edit =
                  { Tag = "EditNode"
                    Category = "op"
                    Fields = [ f "newKind" TKind Required ] }

              Expect.equal
                  (severities (diffOf (ops [ insert ]) (ops [ insert; edit ])))
                  [ Diff.Additive ]
                  "a new op branch leaves every existing stream valid"

              let removal = diffOf (ops [ insert; edit ]) (ops [ insert ])
              Expect.contains (severities removal) Diff.BreakingWire "removing one invalidates persisted streams"

              Expect.stringContains
                  (removal
                   |> List.find (fun c -> c.Change = Diff.OpRemoved "EditNode")
                   |> _.Rationale)
                  "hash-chained archive"
                  "an op stream is not a live message — the rationale must say which it is"

          // --- the node envelope + authoring defaults --------------------------

          testCase "a node-envelope field is diffed like any other"
          <| fun _ ->
              let env fs = { empty with NodeFields = fs }

              let cs = diffOf (env []) (env [ f "style" TJson Optional ])
              Expect.equal (severities cs) [ Diff.Additive ] "optional envelope slot, omitted when absent"

              Expect.stringContains
                  (cs
                   |> List.head
                   |> _.Change
                   |> fun c -> (reportOf (env []) (env [ f "style" TJson Optional ])))
                  "node envelope"
                  "and it is attributed to the envelope, not to a phantom kind"

          testCase "an authoring default is distinguished from a wire default"
          <| fun _ ->
              let withDefault v =
                  { oneKind with
                      Defaults =
                          [ { Kind = "Heading"
                              Field = "text"
                              Value = VStr v } ] }

              let cs = diffOf (withDefault "a") (withDefault "b")

              Expect.equal
                  (severities cs)
                  [ Diff.BreakingForEmitters ]
                  "authoring sites that omitted the field now emit different bytes"

              Expect.stringContains
                  (cs |> List.head |> _.Rationale)
                  "wire contract is unchanged"
                  "but the WIRE is not what moved, and the report must not conflate the two"

          // --- the roster ------------------------------------------------------

          testCase "the roster is read from the manifest when it carries one"
          <| fun _ ->
              let manifest =
                  Canon.render (
                      JObj
                          [ "hosts",
                            JArr
                                [ JObj [ "id", JStr "fuaran"; "language", JStr "F#"; "role", JStr "codec" ]
                                  JObj [ "id", JStr "fuaran-zig"; "language", JStr "Zig"; "role", JStr "codec" ] ] ]
                  )

              let after =
                  { oneKind with
                      Kinds = oneKind.Kinds @ [ kind "Badge" [ f "label" TStr Required ] ] }

              match Diff.run (Some manifest) (art oneKind) (art after) with
              | Ok text ->
                  Expect.stringContains text "manifest.json `hosts`" "the report names where the roster came from"
                  Expect.stringContains text "codec: fuaran-zig (Zig)" "and obligates the host it found"

                  Expect.isFalse
                      (text.Contains "codec: fuaran-py")
                      "a manifest roster REPLACES the declared list rather than merging with it"
              | Error e -> failtestf "idl-diff: %s" e

          testCase "without a manifest roster the report says the list is declared"
          <| fun _ ->
              let after =
                  { oneKind with
                      Kinds = oneKind.Kinds @ [ kind "Badge" [ f "label" TStr Required ] ] }

              Expect.stringContains
                  (reportOf oneKind after)
                  "declared (WIRE_FORMAT.md §11.0"
                  "an unanchored roster must be visible as unanchored, not silently authoritative"

          // --- the live vocabulary ---------------------------------------------

          testCase "the live UI vocabulary reads back through the artifact"
          <| fun _ ->
              match Diff.parse (art uiIdl) with
              | Ok s ->
                  Expect.equal (Map.count s.Kinds) uiIdl.Kinds.Length "every kind survives the artifact round-trip"
                  Expect.equal (Map.count s.Ops) uiIdl.Ops.Length "every op too"
                  Expect.equal (Map.count s.Unions) uiIdl.Unions.Length "every union"
                  Expect.equal (Map.count s.Enums) uiIdl.Enums.Length "every closed set"
                  Expect.equal (Map.count s.Records) uiIdl.Records.Length "every record"
                  Expect.equal (List.length s.NodeFields) uiIdl.NodeFields.Length "and the node envelope"
              | Error e -> failtestf "the live artifact did not read back: %s" e

          testCase "a single added kind on the live vocabulary reports one change"
          <| fun _ ->
              // The realistic case, at real scale: the classifier must not drown
              // a one-kind delta in noise from the other ~40.
              // A tag the live vocabulary does not already carry — appending a
              // duplicate tag is a different (and also correctly-reported) case.
              let after =
                  { uiIdl with
                      Kinds = uiIdl.Kinds @ [ kind "Waveform" [ f "values" (TList TFloat) Required ] ] }

              let cs = diffOf uiIdl after
              Expect.equal (List.length cs) 1 "one change, not a re-listing of the vocabulary"
              Expect.equal (severities cs) [ Diff.Additive ] "and it is additive" ]
