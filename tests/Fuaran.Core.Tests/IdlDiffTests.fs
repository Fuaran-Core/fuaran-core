module Fuaran.Core.Tests.IdlDiffTests

open Expecto
open Fuaran.Core
open Fuaran.Core.Idl
open Fuaran.Core.Tests.ReferenceIdl
open Fuaran.Core.Idl.Codegen.Types

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

/// Render an `Idl` the way a domain renders its committed `idl.json`, so the diff
/// reads real artifact bytes.
let private art (idl: Idl) = Artifact.render idl

let private empty: Idl =
    { Kinds = []
      Unions = []
      Enums = []
      Records = []
      Defaults = []
      NodeFields = []
      Ops = []
      Wire = WireShape.Default
      Harden = HardenPolicy.Undeclared }

let private kind tag fields : IdlKind =
    { Tag = tag
      Category = "display"
      Annotations = Annotations.Empty
      Fields = fields }

let private f name ty opt : IdlField =
    { Name = name
      Type = ty
      Opt = opt
      Aliases = []
      Keyed = false
      Presence = None
      Annotations = Annotations.Empty }

/// Diff two `Idl` values through the artifact, as the CLI does.
let private diffOf (before: Idl) (after: Idl) =
    match Diff.parse (art before) None, Diff.parse (art after) None with
    | Ok b, Ok a -> Diff.changes b a |> List.map Diff.classify
    | Error e, _
    | _, Error e -> failtestf "artifact did not read back: %s" e

let private reportOf (before: Idl) (after: Idl) =
    match Diff.run None (art before, None) (art after, None) with
    | Ok(text, _) -> text
    | Error e -> failtestf "idl-diff: %s" e

let private severities (cs: Classification list) = cs |> List.map _.Severity

/// The UI vocabulary's §11.0 roster, as its manifest declares it — the roster these cases
/// read the UI tier's rows against. Phase 524 moved it here: `Diff.declaredRoster` hard-coded
/// one vocabulary's hosts in the generic classifier and had no caller outside this suite.
let private uiRoster: Host list =
    [ "fuaran", "F#", CodecHost
      "fuaran-ts", "TypeScript", CodecHost
      "fuaran-py", "Python", CodecHost
      "fuaran-go", "Go", CodecHost
      "fuaran-rs", "Rust", CodecHost
      "fuaran-swift", "Swift", RenderProjection
      "fuaran-kt", "Kotlin", RenderProjection ]
    |> List.map (fun (id, language, role) ->
        { Id = id
          Language = language
          Role = role })

/// The surfaces the consolidated obligation set names, at any strength.
let private surfaces (before: Idl) (after: Idl) =
    diffOf before after
    |> List.collect (Diff.obligations uiRoster)
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
              Expect.isEmpty (diffOf refIdl refIdl) "a whole vocabulary against itself is not a change"

              Expect.stringContains
                  (reportOf refIdl refIdl)
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
              Expect.equal (severities cs) [ Additive ] "adding a kind is a minor, not a break"

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
                  BreakingWire
                  "retiring a $type discriminator is a /v2/ major (VOCABULARY.md §4.2)"

              Expect.stringContains (reportOf oneKind empty) "/v2/` MAJOR" "and the profile recommendation says so"

          testCase "a rename is INFERRED beside the add and remove, never instead of them"
          <| fun _ ->
              let after =
                  { empty with
                      Kinds = [ kind "Title" [ f "text" TStr Required ] ] }

              let cs = diffOf oneKind after
              let kinds = cs |> List.map _.Change

              Expect.contains kinds (KindRenamed("Heading", "Title")) "the identical signature pairs uniquely"
              Expect.contains kinds (KindRemoved "Heading") "and the removal still stands on its own"
              Expect.contains kinds (KindAdded "Title") "as does the addition"

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
                      | KindRenamed _ -> true
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
                      | KindRenamed _ -> true
                      | _ -> false)

              Expect.isEmpty renames "two candidates on each side is not a rename, it is a guess"

          // --- the classification a human gets wrong -------------------------

          testCase "a REQUIRED field added breaks the WIRE, not additive and not merely emitters"
          <| fun _ ->
              // Phase 304: graded by what an OLD document does under the new vocabulary — every
              // stored `Heading` lacks `level`, and the decoder refuses a missing required member.
              let after =
                  { empty with
                      Kinds = [ kind "Heading" [ f "text" TStr Required; f "level" TInt Required ] ] }

              let cs = diffOf oneKind after
              Expect.equal (severities cs) [ BreakingWire ] "old documents are refused: a /v2/ event"

              Expect.stringContains
                  (cs |> List.head |> _.Rationale)
                  "refuses"
                  "and the rationale names what the old document does"

              let report = reportOf oneKind after
              Expect.stringContains report "stability_impact: breaking" "so the draft front-matter says breaking"

          testCase "an optional field added is additive"
          <| fun _ ->
              let after =
                  { empty with
                      Kinds = [ kind "Heading" [ f "text" TStr Required; f "level" TInt Optional ] ] }

              Expect.equal (severities (diffOf oneKind after)) [ Additive ] "omitted when absent, so nothing breaks"

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
                  [ BreakingWire ]
                  "omit-at-default is wire-visible: every document sitting on the old default changes bytes"

              Expect.stringContains
                  (cs |> List.head |> _.Rationale)
                  "WIRE-VISIBLE"
                  "and the rationale says why, since this is the row most likely to be waved through"

          testCase "required -> optional is additive: every old document carries the member and re-encodes identically"
          <| fun _ ->
              // Phase 304 — this row was `BreakingWire` on the consumer-presence argument. Graded by
              // what an OLD document does, it is additive: every one carries the member, decodes to
              // the same value and re-encodes byte-identically, and every old emitter writes it. The
              // consumer that relied on presence meets absence only from a NEW emitter — host lag,
              // which the rationale records.
              let after =
                  { empty with
                      Kinds = [ kind "Heading" [ f "text" TStr Optional ] ] }

              let cs = diffOf oneKind after
              Expect.equal (severities cs) [ Additive ] "old documents are untouched"

              Expect.stringContains
                  (cs |> List.head |> _.Rationale)
                  "host lag"
                  "and the consumer-presence cost is named, not dropped"

          testCase "required -> omitDefault stays a wire event: a document on the default changes bytes"
          <| fun _ ->
              let after =
                  { empty with
                      Kinds = [ kind "Heading" [ f "text" TStr (OmitDefault(VStr "")) ] ] }

              let cs = diffOf oneKind after
              Expect.equal (severities cs) [ BreakingWire ] "omit-at-default is wire-visible"

              Expect.stringContains
                  (cs |> List.head |> _.Rationale)
                  "bytes move"
                  "and the rationale names what the old document does"

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
                  [ HostSurfaceOnly ]
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
                                          { Refusal = Fuaran.Core.Idl.HostedRefusal.Sentence
                                            FSharp = "HostPrelude.LiveRegionKind"
                                            Encode = "encLiveRegionKind"
                                            Decode = "decLiveRegionKind"
                                            Wire = None
                                            Format = None })
                                      Optional ]
                              Annotations = Annotations.Empty
                              Inline = false } ] }

              let declared =
                  { empty with
                      Enums = [ Declare.enumWith "LiveRegionKind" [ "Polite", "polite" ] ]
                      Records =
                          [ { Name = "Accessibility"
                              Fields = [ f "liveRegion" (TEnum "LiveRegionKind") Optional ]
                              Annotations = Annotations.Empty
                              Inline = false } ] }

              let cs = diffOf hosted declared

              let typeChange =
                  cs
                  |> List.find (fun c ->
                      match c.Change with
                      | FieldTypeChanged _ -> true
                      | _ -> false)

              Expect.equal
                  typeChange.Severity
                  Unclassifiable
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
                  [ BreakingWire ]
                  "int -> str is fully described by the artifact and decodes differently"

          testCase "a category re-classification is metadata, never serialised"
          <| fun _ ->
              let after =
                  { empty with
                      Kinds =
                          [ { kind "Heading" [ f "text" TStr Required ] with
                                Category = "layout"
                                Annotations = Annotations.Empty } ] }

              Expect.equal (severities (diffOf oneKind after)) [ HostSurfaceOnly ] "Category is IDL metadata"

          // --- the other discriminator families --------------------------------

          testCase "a union case addition carries the identical §11 wire cost"
          <| fun _ ->
              let union cases =
                  { empty with
                      Unions =
                          [ { TagRule = Fuaran.Core.Idl.UnionTagRule.CaseName
                              Name = "Binding"
                              Params = [ "T" ]
                              Cases = cases
                              Annotations = Annotations.Empty
                              Discrimination = UnionDiscrimination.Tagged } ] }

              let stat: IdlUnionCase =
                  { Hosted = None
                    Tag = "Static"
                    Fields = [ f "value" TStr Required ]
                    Annotations = Annotations.Empty }

              let state: IdlUnionCase =
                  { Hosted = None
                    Tag = "State"
                    Fields = [ f "key" TStr Required ]
                    Annotations = Annotations.Empty }

              let before, after = union [ stat ], union [ stat; state ]
              let cs = diffOf before after

              Expect.contains (severities cs) Additive "additive on the wire"

              Expect.stringContains
                  (cs
                   |> List.find (fun c -> c.Change = UnionCaseAdded("Binding", "State"))
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

              Expect.equal (severities cs) [ Additive ] "a wider closed set admits every old document"

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
                  BreakingWire
                  "\"Polite\" is no longer admitted and \"polite\" newly is"

              // Same wire strings, different F# case names: hostSurface only.
              Expect.equal
                  (severities (diffOf remappedWire remappedHost))
                  [ HostSurfaceOnly ]
                  "hostCases is a §13 hostSurface key — a source-compat event, not a wire one"

          testCase "an op addition is additive; an op removal is worse than a kind removal"
          <| fun _ ->
              let ops os = { empty with Ops = os }

              let insert =
                  { Tag = "InsertChild"
                    Category = "op"
                    Annotations = Annotations.Empty
                    Fields = [ f "child" TNode Required ] }

              let edit =
                  { Tag = "EditNode"
                    Category = "op"
                    Annotations = Annotations.Empty
                    Fields = [ f "newKind" TKind Required ] }

              Expect.equal
                  (severities (diffOf (ops [ insert ]) (ops [ insert; edit ])))
                  [ Additive ]
                  "a new op branch leaves every existing stream valid"

              let removal = diffOf (ops [ insert; edit ]) (ops [ insert ])
              Expect.contains (severities removal) BreakingWire "removing one invalidates persisted streams"

              Expect.stringContains
                  (removal |> List.find (fun c -> c.Change = OpRemoved "EditNode") |> _.Rationale)
                  "hash-chained archive"
                  "an op stream is not a live message — the rationale must say which it is"

          // --- the node envelope + authoring defaults --------------------------

          testCase "a node-envelope field is diffed like any other"
          <| fun _ ->
              let env fs = { empty with NodeFields = fs }

              let cs = diffOf (env []) (env [ f "style" TJson Optional ])
              Expect.equal (severities cs) [ Additive ] "optional envelope slot, omitted when absent"

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
                  [ BreakingForEmitters ]
                  "authoring sites that omitted the field now emit different bytes"

              Expect.stringContains
                  (cs |> List.head |> _.Rationale)
                  "wire contract is unchanged"
                  "but the WIRE is not what moved, and the report must not conflate the two"

          // --- the roster ------------------------------------------------------

          testCase "the roster is read from the manifest when it carries one"
          <| fun _ ->
              let manifest =
                  Canonical.write
                      Rendering.CrossHost
                      (JObj
                          [ "hosts",
                            JArr
                                [ JObj [ "id", JStr "fuaran"; "language", JStr "F#"; "role", JStr "codec" ]
                                  JObj [ "id", JStr "fuaran-zig"; "language", JStr "Zig"; "role", JStr "codec" ] ] ])

              let after =
                  { oneKind with
                      Kinds = oneKind.Kinds @ [ kind "Badge" [ f "label" TStr Required ] ] }

              match Diff.run (Some manifest) (art oneKind, None) (art after, None) with
              | Ok(text, _) ->
                  Expect.stringContains text "manifest.json `hosts`" "the report names where the roster came from"
                  Expect.stringContains text "codec: fuaran-zig (Zig)" "and obligates the host it found"

                  Expect.isFalse
                      (text.Contains "codec: fuaran-py")
                      "a manifest roster REPLACES the declared list rather than merging with it"
              | Error e -> failtestf "idl-diff: %s" e

          testCase "without a manifest roster no host is obliged by name, and the report says so"
          <| fun _ ->
              // Phase 252 — the roster used to fall back to the UI vocabulary's hosts, so a
              // vocabulary with none of them was told to change all of them.
              let after =
                  { oneKind with
                      Kinds = oneKind.Kinds @ [ kind "Badge" [ f "label" TStr Required ] ] }

              let text = reportOf oneKind after

              Expect.stringContains
                  text
                  "none declared (no manifest given)"
                  "an absent roster must be visible as absent, not silently filled"

              Expect.stringContains text "hosts: none declared" "the obligation set says no host is named"

              for uiOnly in [ "fuaran-py"; "JsonDecode.fs"; "wire-format-fixtures"; "Fuaran.UI" ] do
                  Expect.isFalse (text.Contains uiOnly) (sprintf "no UI-tier obligation (%s) without its host" uiOnly)

          testCase "a manifest without `hosts` obliges no host either"
          <| fun _ ->
              let after =
                  { oneKind with
                      Kinds = oneKind.Kinds @ [ kind "Badge" [ f "label" TStr Required ] ] }

              match
                  Diff.run
                      (Some(Canonical.write Rendering.CrossHost (JObj [ "kinds", JArr [] ])))
                      (art oneKind, None)
                      (art after, None)
              with
              | Ok(text, _) ->
                  Expect.stringContains text "none declared (the manifest carries no `hosts` key)" "names the cause"
                  Expect.isFalse (text.Contains "codec: fuaran") "no default roster"
              | Error e -> failtestf "idl-diff: %s" e

          testCase "Phase 524 — `run` renders the one verdict `ofArtifacts` computes, on every roster source"
          <| fun _ ->
              // One place resolves the roster and one computes the verdict. `run` carried a copy of
              // the roster until Phase 348 and parsed each side twice until Phase 524; this pins that
              // the pipeline and the door cannot drift apart: the same verdict, or the same refusal,
              // for each way a manifest can name a roster, and the report ends in its verdict block.
              let after =
                  { oneKind with
                      Kinds = oneKind.Kinds @ [ kind "Badge" [ f "label" TStr Required ] ] }

              let withHosts =
                  Canonical.write
                      Rendering.CrossHost
                      (JObj
                          [ "hosts",
                            JArr [ JObj [ "id", JStr "fuaran-zig"; "language", JStr "Zig"; "role", JStr "codec" ] ] ])

              let manifests =
                  [ None
                    Some(Canonical.write Rendering.CrossHost (JObj [ "kinds", JArr [] ]))
                    Some withHosts
                    Some "not json" ]

              for m in manifests do
                  for before, afterText in [ art oneKind, art after; art oneKind, "{"; "{", art after ] do
                      let run = Diff.run m (before, None) (afterText, None)

                      Expect.equal
                          (run |> Result.map snd)
                          (Diff.ofArtifacts (before, None) (afterText, None))
                          (sprintf "run and ofArtifacts agree (manifest %A)" m)

                      match run with
                      | Ok(text, v) ->
                          Expect.isTrue (text.EndsWith(Diff.verdictBlock v)) "the report ends in the verdict's block"
                      | Error _ -> ()

              match Diff.run (Some withHosts) (art oneKind, None) (art after, None) with
              | Ok(text, _) -> Expect.stringContains text "codec: fuaran-zig (Zig)" "the probe compared a real report"
              | Error e -> failtestf "idl-diff: %s" e

          testCase "the UI tier's rows follow its reference host, passed explicitly"
          <| fun _ ->
              let after =
                  { oneKind with
                      Kinds = oneKind.Kinds @ [ kind "Badge" [ f "label" TStr Required ] ] }

              let rows = surfaces oneKind after

              Expect.contains rows "codec: fuaran-py (Python)" "the declared UI roster still obliges its hosts"
              Expect.contains rows "corpus: wire-format-fixtures fixture" "and its corpus row"

          // --- a whole vocabulary, not a two-kind fixture -----------------------
          //
          // Phase 123: these two cases read the engine's domain-neutral reference
          // vocabulary. They used to read the UI fixture, which was the only
          // full-scale `Idl` this repo held; that fixture now lives in the domain's
          // own repository (DECISIONS.md D14). What they certify is unchanged —
          // that the classifier reads a complete artifact back, and reports a
          // one-kind delta as one change rather than re-listing the vocabulary
          // around it. The vocabulary is smaller, so the noise floor they probe is
          // lower; certifying that at a domain's scale is the domain's own gate.

          testCase "a whole vocabulary reads back through the artifact"
          <| fun _ ->
              match Diff.parse (art refIdl) None with
              | Ok s ->
                  Expect.equal (Map.count s.Kinds) refIdl.Kinds.Length "every kind survives the artifact round-trip"
                  Expect.equal (Map.count s.Ops) refIdl.Ops.Length "every op too"
                  Expect.equal (Map.count s.Unions) refIdl.Unions.Length "every union"
                  Expect.equal (Map.count s.Enums) refIdl.Enums.Length "every closed set"
                  Expect.equal (Map.count s.Records) refIdl.Records.Length "every record"
                  Expect.equal (List.length s.NodeFields) refIdl.NodeFields.Length "and the node envelope"
              | Error e -> failtestf "the artifact did not read back: %s" e

          testCase "a single added kind on a whole vocabulary reports one change"
          <| fun _ ->
              // A tag the vocabulary does not already carry — appending a
              // duplicate tag is a different (and also correctly-reported) case.
              let after =
                  { refIdl with
                      Kinds = refIdl.Kinds @ [ kind "Waveform" [ f "values" (TList TFloat) Required ] ] }

              let cs = diffOf refIdl after
              Expect.equal (List.length cs) 1 "one change, not a re-listing of the vocabulary"
              Expect.equal (severities cs) [ Additive ] "and it is additive" ]

// ---------------------------------------------------------------------------
// Phase 252 — two classifier rules corrected.
//
// Int to float is a WIDENING: a float slot admits every integer and a whole float
// renders as the same digits, so every old document decodes and re-encodes
// byte-identically and every old emitter stays conformant — the table's definition
// of additive (the 2026-09-30 amendment; the cost is host lag, as for a new enum
// case). And a hosted slot's codec IS its wire form, so a move in its declaration
// is undecided (exit 4), never an absorbable host-surface change.
// ---------------------------------------------------------------------------

let private oneField (ty: IdlType) =
    { empty with
        Kinds = [ kind "Weight" [ f "grams" ty Required ] ] }

let private hostedField (h: HostedCodec) = oneField (THosted h)

let private dateCodec: HostedCodec =
    { Refusal = Fuaran.Core.Idl.HostedRefusal.Sentence
      FSharp = "System.DateOnly"
      Encode = "HostDate.encode"
      Decode = "HostDate.decode"
      Wire = None
      Format = None }

let private consequencesOf (before: Idl) (after: Idl) =
    diffOf before after |> List.collect Diff.consequences |> List.distinct

let private exitOf (before: Idl) (after: Idl) =
    match Diff.ofValues (before, None) (after, None) with
    | Ok v -> Diff.exitCode (Diff.verdictClass v)
    | Error e -> failtestf "classify: %s" e

[<Tests>]
let correctedRuleTests =
    testList
        "Phase 252 — int-to-float widening and the hosted codec"
        [ testCase "int to float is an additive widening, a construction break, exit 0" (fun _ ->
              Expect.equal (severities (diffOf (oneField TInt) (oneField TFloat))) [ Additive ] "additive"

              Expect.contains
                  (consequencesOf (oneField TInt) (oneField TFloat))
                  FullLiteralConstruction
                  "the generated field moved from int to float"

              Expect.equal (exitOf (oneField TInt) (oneField TFloat)) 0 "a gate absorbs it")

          testCase "the widening reaches a list element, and nothing else widens" (fun _ ->
              Expect.equal
                  (severities (diffOf (oneField (TList TInt)) (oneField (TList TFloat))))
                  [ Additive ]
                  "list<int> to list<float>"

              Expect.equal (severities (diffOf (oneField TFloat) (oneField TInt))) [ BreakingWire ] "narrowing breaks"

              Expect.equal (severities (diffOf (oneField TInt) (oneField TStr))) [ BreakingWire ] "a retype breaks"

              Expect.equal
                  (severities (diffOf (oneField (TList TInt)) (oneField (TMap TFloat))))
                  [ BreakingWire ]
                  "a widening inside a different container is not a widening")

          testCase "a hosted slot's codec swap is undecided, exit 4, and moves no generated shape" (fun _ ->
              let before = hostedField dateCodec

              let after =
                  hostedField
                      { dateCodec with
                          Encode = "HostDate.encodeEpochDays"
                          Decode = "HostDate.decodeEpochDays" }

              Expect.equal (severities (diffOf before after)) [ Unclassifiable ] "undecided"
              Expect.equal (exitOf before after) 4 "a gate stops"
              Expect.equal (consequencesOf before after) [ NoGeneratedShapeChange ] "the F# type did not move")

          testCase "a hosted slot's host-type change is undecided too, and a construction break" (fun _ ->
              let before = hostedField dateCodec

              let after =
                  hostedField
                      { dateCodec with
                          FSharp = "System.DateTime" }

              Expect.equal (severities (diffOf before after)) [ Unclassifiable ] "undecided"
              Expect.contains (consequencesOf before after) FullLiteralConstruction "the field's type moved")

          testCase "a closure's signature stays host-surface only" (fun _ ->
              let fn ts =
                  oneField (
                      TFn
                          { FSharp = "unit -> unit"
                            TypeScript = ts
                            Placeholder = "ignore" }
                  )

              Expect.equal
                  (severities (diffOf (fn "() => void") (fn "() => unknown")))
                  [ HostSurfaceOnly ]
                  "host-surface"

              Expect.equal
                  (consequencesOf (fn "() => void") (fn "() => unknown"))
                  [ NoGeneratedShapeChange ]
                  "a TypeScript-only move leaves the F# shape where it was") ]

// ---------------------------------------------------------------------------
// Phase 293 — the descriptor table: the erased-slot rule walks nested slots, an authoring
// default's consequence follows the field's optionality, and the three verdict readers
// share one precedence.
// ---------------------------------------------------------------------------

let private verdictOf (before: Idl) (after: Idl) =
    match Diff.ofValues (before, None) (after, None) with
    | Ok v -> v
    | Error e -> failtestf "classify: %s" e

let private withDefaults (idl: Idl) (defaults: IdlDefault list) = { idl with Defaults = defaults }

[<Tests>]
let descriptorTableTests =
    testList
        "Phase 293 — the classifier's descriptor table"
        [ testCase
              "the descriptor table has a rule for every Change case (Phase 384: the suite holds it, not ruleOf)"
              (fun _ ->
                  // `ruleOf` used to reflect over `Change` and raise on its first use when a case had no
                  // rule — a completeness check of this SOURCE executed on a consumer's machine. It is
                  // held here instead. Every row of `mappingTable` is a projection of one rule and names
                  // its case, so a case named by no row is a case with no rule (or one that documents
                  // nothing, which the generated `docs/` table must not omit either).
                  let cases =
                      Microsoft.FSharp.Reflection.FSharpType.GetUnionCases(
                          typeof<Change>,
                          System.Reflection.BindingFlags.Public
                      )
                      |> Array.map _.Name
                      |> Set.ofArray

                  let ruled =
                      Diff.mappingTable.Split('\n')
                      |> Array.choose (fun line ->
                          let cells = line.Split('|') |> Array.map _.Trim()

                          // `| Change | Wire severity | F# consequence | Classifier case |` — the last
                          // cell, back-quoted, on every data row.
                          let last = if cells.Length >= 6 then cells[cells.Length - 2] else ""

                          if last.StartsWith "`" && last.EndsWith "`" then
                              Some(last.Trim('`'))
                          else
                              None)
                      |> Set.ofArray

                  Expect.isGreaterThan cases.Count 10 "the reflection read the union (the probe saw its cases)"
                  Expect.equal (Set.difference cases ruled) Set.empty "every Change case has a rule"
                  Expect.equal (Set.difference ruled cases) Set.empty "and every rule names a Change case")

          testCase "Phase 524 — `Diff.verdict` over two parsed snapshots IS the verdict `ofArtifacts` answers" (fun _ ->
              let after =
                  { oneKind with
                      Kinds = oneKind.Kinds @ [ kind "Badge" [ f "label" TStr Optional ] ] }

              match Diff.parse (art oneKind) None, Diff.parse (art after) None with
              | Ok b, Ok a ->
                  let v = Diff.verdict b a
                  Expect.equal (Ok v) (Diff.ofArtifacts (art oneKind, None) (art after, None)) "one computation"
                  Expect.equal v.Encoding (b.Version, a.Version) "the verdict carries both encodings"
                  Expect.isNonEmpty v.Changes "the probe compared a real change"
              | _ -> failtest "the artifacts did not read back")

          testCase
              "Phase 524 — the rule is ONE match, and its exemplars name every Change case exactly once, each by its own name"
              (fun _ ->
                  // `mappingTable` enumerates `DiffRules.exemplars` through `ruleOf`, so the table above is
                  // complete exactly when the exemplars are: every case once, and each arm labels itself
                  // with its own case (an arm copied from its neighbour and not relabelled is caught here).
                  let caseOf (c: Change) =
                      (Microsoft.FSharp.Reflection.FSharpValue.GetUnionFields(c, typeof<Change>) |> fst).Name

                  let cases =
                      Microsoft.FSharp.Reflection.FSharpType.GetUnionCases typeof<Change>
                      |> Array.map _.Name
                      |> List.ofArray

                  let exemplars = Fuaran.Core.Idl.Emit.DiffRules.exemplars |> List.map caseOf
                  Expect.isGreaterThan cases.Length 10 "the reflection read the union"
                  Expect.equal (List.sort exemplars) (List.sort cases) "every case exactly once"

                  for e in Fuaran.Core.Idl.Emit.DiffRules.exemplars do
                      Expect.equal
                          (Fuaran.Core.Idl.Emit.DiffRules.ruleOf e).Case
                          (caseOf e)
                          "the arm names its own case")

          testCase "a type change across a NESTED erased slot is undecided, exit 4, and shape-unreadable" (fun _ ->
              // The rule used to test the top-level tag alone, so a list of hosted values
              // moving to a list of verbatim JSON reported `breaking-wire` where the document
              // said undecided — the artifact states nothing about what either slot admits.
              let before = oneField (TList(THosted dateCodec))
              let after = oneField (TList TJson)
              Expect.equal (severities (diffOf before after)) [ Unclassifiable ] "undecided, not breaking"
              Expect.equal (exitOf before after) 4 "exit 4"

              Expect.equal
                  (consequencesOf before after)
                  [ GeneratedShapeUnreadable ]
                  "and the F# axis is reported unreadable rather than guessed")

          testCase "a nested type change that crosses no erased slot is still decided" (fun _ ->
              let before = oneField (TList TStr)
              let after = oneField (TList TInt)

              Expect.equal (severities (diffOf before after)) [ BreakingWire ] "a value that decoded no longer does"

              Expect.equal (exitOf before after) 3 "exit 3")

          testCase "a map value and a union argument are walked too" (fun _ ->
              let before = oneField (TMap(THosted dateCodec))
              let after = oneField (TMap TOpaque)
              Expect.equal (severities (diffOf before after)) [ Unclassifiable ] "a map's value slot")

          testCase
              "an authoring default on a REQUIRED field is a construction break — `mk<Kind>` loses the parameter"
              (fun _ ->
                  let before = oneField TStr

                  let after =
                      withDefaults
                          before
                          [ { Kind = "Weight"
                              Field = "grams"
                              Value = VStr "0" } ]

                  let v = verdictOf before after

                  Expect.contains
                      v.FSharpConsequences
                      FullLiteralConstruction
                      "every `mkWeight` call site stops compiling: the parameter is gone"

                  Expect.contains
                      v.FSharpConsequences
                      StalePackageSlot
                      "a shape change carries the stale-slot consequence"

                  let removed = verdictOf after before

                  Expect.contains
                      removed.FSharpConsequences
                      FullLiteralConstruction
                      "and removing it brings the parameter back, which breaks every call site again")

          testCase "an authoring default on an OPTIONAL field moves no generated shape" (fun _ ->
              let before =
                  { empty with
                      Kinds = [ kind "Weight" [ f "grams" TStr Required; f "label" TStr Optional ] ] }

              let after =
                  withDefaults
                      before
                      [ { Kind = "Weight"
                          Field = "label"
                          Value = VStr "kg" } ]

              Expect.equal
                  (verdictOf before after).FSharpConsequences
                  [ NoGeneratedShapeChange ]
                  "the constructor's body moves; its parameter list does not")

          testCase
              "without a snapshot to ask, the context-free `consequences` reads a default as a required field's"
              (fun _ ->
                  let c = Diff.classify (DefaultAdded("Weight", "grams", "0"))

                  Expect.equal
                      (Diff.consequences c)
                      [ FullLiteralConstruction; StalePackageSlot ]
                      "the answer that costs a rebuild rather than a surprise")

          testCase
              "an undecided row heads the verdict even beside a breaking one, and the drafts say what is certain"
              (fun _ ->
                  // One precedence for the class, the front-matter draft and the profile advice:
                  // undecided dominates the CLASS, and the two prose drafts report the breaking
                  // remainder as certain rather than as conditional on the undecided row's check.
                  let before =
                      { empty with
                          Kinds = [ kind "Weight" [ f "grams" TStr Required; f "when" (THosted dateCodec) Required ] ] }

                  let after =
                      { empty with
                          Kinds = [ kind "Weight" [ f "grams" TInt Required; f "when" TJson Required ] ] }

                  let v = verdictOf before after
                  Expect.equal (Diff.verdictClass v) VerdictClass.Undecided "undecided heads the class"

                  Expect.equal
                      v.StabilityImpact
                      "breaking"
                      "the draft impact is certain: a breaking row stands whatever the check finds"

                  Expect.stringContains v.ProfileAdvice "MAJOR" "and so is the profile bump")

          testCase
              "the report orders its rows by the descriptor table's rank, host-surface rows last among equals"
              (fun _ ->
                  let before =
                      { empty with
                          Kinds = [ kind "A" [ f "x" TStr Required ] ] }

                  let after =
                      { empty with
                          Kinds = [ kind "A" [ f "x" TStr Required ]; kind "B" [] ]
                          Enums =
                              [ { Name = "E"
                                  Cases = [ "P" ]
                                  Wires = []
                                  CaseAnnotations = []
                                  Aliases = []
                                  Annotations = Annotations.Empty } ] }

                  let cs = diffOf before after |> List.map _.Change

                  Expect.equal
                      cs
                      [ KindAdded "B"; EnumAdded "E" ]
                      "a kind (rank 10) sorts before an enum (rank 40), as the table ranks them") ]

// ---------------------------------------------------------------------------
// Phase 293 — support.json joins the classifier's inputs.
// ---------------------------------------------------------------------------

let private supportVerdict (before: SupportDocument option) (after: SupportDocument option) =
    match Diff.ofValues (refIdl, before) (refIdl, after) with
    | Ok v -> v
    | Error e -> failtestf "ofValues: %s" e

[<Tests>]
let supportInputTests =
    testList
        "Phase 293 — declared support is a classifier input"
        [ testCase
              "the same vocabulary with the same support is unchanged, and so is one with no support on either side"
              (fun _ ->
                  Expect.equal
                      (Diff.verdictClass (supportVerdict (Some support) (Some support)))
                      VerdictClass.Unchanged
                      "same support"

                  Expect.equal (Diff.verdictClass (supportVerdict None None)) VerdictClass.Unchanged "no support")

          testCase
              "a kind projection edited beside an unchanged vocabulary is a host-surface row with a construction consequence, not `unchanged`"
              (fun _ ->
                  let edited =
                      { support with
                          Support =
                              { support.Support with
                                  KindProjections =
                                      support.Support.KindProjections
                                      |> Map.map (fun _ p ->
                                          { p with
                                              SpecDecl = p.SpecDecl + " // moved" }) } }

                  let v = supportVerdict (Some support) (Some edited)

                  Expect.equal (v.Changes |> List.map _.Severity) [ HostSurfaceOnly ] "host-surface, never wire"

                  Expect.equal (Diff.verdictClass v) VerdictClass.HostSurface "the class a gate reads"

                  Expect.contains
                      v.FSharpConsequences
                      FullLiteralConstruction
                      "a projection supplies the generated record, so a move there moves declarations"

                  match v.Changes.Head.Change with
                  | SupportChanged(key, Some _, Some _) ->
                      Expect.equal key "projection:Note" "keyed by the projected kind"
                  | other -> failtestf "expected a projection change, got %A" other)

          testCase "a case refine edited is host-surface with no generated shape change" (fun _ ->
              let edited =
                  { support with
                      Support =
                          { support.Support with
                              CaseRefines = support.Support.CaseRefines |> Map.map (fun _ e -> e + " (* refined *)") } }

              let v = supportVerdict (Some support) (Some edited)
              Expect.equal (v.Changes |> List.map _.Severity) [ HostSurfaceOnly ] "host-surface"

              Expect.equal
                  v.FSharpConsequences
                  [ NoGeneratedShapeChange ]
                  "a decoder arm's final expression moves no declaration")

          testCase
              "support arriving where there was none reports every entry as added, and the artifact-only door still reads unchanged"
              (fun _ ->
                  let v = supportVerdict None (Some support)
                  Expect.isNonEmpty v.Changes "every declared entry is an added row"

                  for c in v.Changes do
                      match c.Change with
                      | SupportChanged(_, None, Some _) -> ()
                      | other -> failtestf "expected an added support entry, got %A" other

                  match Diff.ofValues (refIdl, None) (refIdl, None) with
                  | Ok plain ->
                      Expect.equal
                          (Diff.verdictClass plain)
                          VerdictClass.Unchanged
                          "the door with no support sees none"
                  | Error e -> failtestf "ofValues: %s" e)

          testCase "the mapping table carries the support row" (fun _ ->
              Expect.stringContains Diff.mappingTable "`SupportChanged`" "a row for the case") ]

// ---------------------------------------------------------------------------
// Phase 548 — a TYPE's own annotations (Phase 542) are read and graded. A union, a record or an
// enum gaining, losing or changing its marking is graded exactly as the same move on one of its
// members is, and an unannotated pair yields no new change.
// ---------------------------------------------------------------------------

let private marked: Annotations =
    { Annotations.Empty with
        Deprecated =
            Some
                { Replacement = None
                  Message = Some "retired in the next release" } }

let private documented (text: string) : Annotations =
    { Annotations.Empty with
        Doc = Some text }

/// One union, one record and one enum, the type-level annotations of the named family set to
/// `typeAnn` and the `value` field of the record (the member the type-level grade is compared
/// with) set to `fieldAnn`.
let private typed (family: string) (typeAnn: Annotations) (fieldAnn: Annotations) : Idl =
    let on fam =
        if fam = family then typeAnn else Annotations.Empty

    { empty with
        Unions =
            [ { TagRule = Fuaran.Core.Idl.UnionTagRule.CaseName
                Name = "Source"
                Params = []
                Cases =
                  [ { Hosted = None
                      Tag = "Lit"
                      Fields = [ f "text" TStr Required ]
                      Annotations = Annotations.Empty } ]
                Annotations = on "union"
                Discrimination = UnionDiscrimination.Tagged } ]
        Records =
            [ { Name = "Span"
                Fields =
                  [ { f "value" TStr Required with
                        Annotations = fieldAnn } ]
                Annotations = on "record"
                Inline = false } ]
        Enums =
            [ { Declare.enumOf "Tone" [ "Quiet"; "Loud" ] with
                  Annotations = on "enum" } ] }

let private typeMoves (cs: Classification list) =
    cs
    |> List.filter (fun c ->
        match c.Change with
        | TypeAnnotationsChanged _ -> true
        | _ -> false)

/// The grade the same annotation move takes on the record's `value` field.
let private memberGrade (before: Annotations) (after: Annotations) : Severity =
    match
        diffOf (typed "" Annotations.Empty before) (typed "" Annotations.Empty after)
        |> List.filter (fun c ->
            match c.Change with
            | FieldAnnotationsChanged(ORecord "Span", "value", _, _) -> true
            | _ -> false)
    with
    | [ c ] -> c.Severity
    | other -> failtestf "expected one field-annotation change, got %A" other

let private names = [ "union", "Source"; "record", "Span"; "enum", "Tone" ]

[<Tests>]
let typeAnnotationTests =
    testList
        "Idl diff — type-level annotations (Phase 548)"
        [ for family, name in names do
              for label, before, after in
                  [ "gaining a deprecation", Annotations.Empty, marked
                    "losing a deprecation", marked, Annotations.Empty
                    "changing only its doc", documented "a span", documented "a run of text" ] do
                  testCase (sprintf "a %s %s is graded as a member's equivalent is" family label) (fun _ ->
                      let cs =
                          diffOf (typed family before Annotations.Empty) (typed family after Annotations.Empty)

                      match typeMoves cs with
                      | [ c ] ->
                          match c.Change with
                          | TypeAnnotationsChanged(fam, n, _, _) ->
                              Expect.equal (fam, n) (family, name) "the type is named by family and name"
                          | other -> failtestf "unexpected %A" other

                          Expect.equal c.Severity (memberGrade before after) "the member's grade, exactly"

                          Expect.equal
                              (Diff.consequences c)
                              [ NoGeneratedShapeChange ]
                              "an annotation moves no generated shape"

                          Expect.hasLength cs 1 "and nothing else is reported"
                      | other -> failtestf "expected one type-annotation change, got %A" other)

          testCase "a first marking is additive and a withdrawal host-surface, as for a member" (fun _ ->
              let grade b a =
                  diffOf (typed "enum" b Annotations.Empty) (typed "enum" a Annotations.Empty)
                  |> typeMoves
                  |> List.map _.Severity

              Expect.equal (grade Annotations.Empty marked) [ Additive ] "marking a whole type breaks nothing"
              Expect.equal (grade marked Annotations.Empty) [ HostSurfaceOnly ] "un-retiring it is a plain change")

          testCase "an unannotated pair of artifacts yields no new change" (fun _ ->
              let plain = typed "" Annotations.Empty Annotations.Empty
              Expect.isEmpty (diffOf plain plain) "identical unannotated artifacts diff to nothing"

              let grown =
                  { plain with
                      Records =
                          plain.Records
                          @ [ { Name = "Extra"
                                Fields = [ f "n" TInt Required ]
                                Annotations = Annotations.Empty
                                Inline = false } ] }

              Expect.isEmpty (typeMoves (diffOf plain grown)) "a type added unannotated is no annotation move")

          testCase "a type that arrives already marked is its addition, not an annotation move" (fun _ ->
              let plain = typed "" Annotations.Empty Annotations.Empty

              let grown =
                  { plain with
                      Records =
                          plain.Records
                          @ [ { Name = "Extra"
                                Fields = [ f "n" TInt Required ]
                                Annotations = marked
                                Inline = false } ] }

              let cs = diffOf plain grown
              Expect.isEmpty (typeMoves cs) "no annotation row for a type the old revision lacks"

              Expect.exists cs (fun c -> c.Change = RecordAdded "Extra") "it is reported as the addition it is")

          testCase "a pre-542 artifact (no type-level key) reads as unannotated" (fun _ ->
              match Diff.parse (art (typed "union" marked Annotations.Empty)) None with
              | Ok snap ->
                  Expect.equal
                      (snap.TypeAnnotations |> Map.find ("union", "Source") <> "")
                      true
                      "the marked union's set is read"

                  Expect.equal (snap.TypeAnnotations |> Map.find ("record", "Span")) "" "absent reads as empty"
              | Error e -> failtestf "artifact did not read back: %s" e)

          testCase "the mapping table carries the type-annotation row" (fun _ ->
              Expect.stringContains Diff.mappingTable "`TypeAnnotationsChanged`" "a row for the case") ]
