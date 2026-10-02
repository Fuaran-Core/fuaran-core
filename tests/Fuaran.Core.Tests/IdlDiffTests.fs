module Fuaran.Core.Tests.IdlDiffTests

open Expecto
open Fuaran.Core
open Fuaran.Core.Idl
open Fuaran.Core.Tests.ReferenceIdl

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
      Annotations = Annotations.Empty }

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

          testCase "a REQUIRED field added breaks the WIRE, not additive and not merely emitters"
          <| fun _ ->
              // Phase 304: graded by what an OLD document does under the new vocabulary — every
              // stored `Heading` lacks `level`, and the decoder refuses a missing required member.
              let after =
                  { empty with
                      Kinds = [ kind "Heading" [ f "text" TStr Required; f "level" TInt Required ] ] }

              let cs = diffOf oneKind after
              Expect.equal (severities cs) [ Diff.BreakingWire ] "old documents are refused: a /v2/ event"

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
              Expect.equal (severities cs) [ Diff.Additive ] "old documents are untouched"

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
              Expect.equal (severities cs) [ Diff.BreakingWire ] "omit-at-default is wire-visible"

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
                                            Decode = "decLiveRegionKind"
                                            Wire = None
                                            Format = None })
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
                                Category = "layout"
                                Annotations = Annotations.Empty } ] }

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
                    Fields = [ f "value" TStr Required ]
                    Annotations = Annotations.Empty }

              let state: IdlUnionCase =
                  { Tag = "State"
                    Fields = [ f "key" TStr Required ]
                    Annotations = Annotations.Empty }

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
                    Annotations = Annotations.Empty
                    Fields = [ f "child" TNode Required ] }

              let edit =
                  { Tag = "EditNode"
                    Category = "op"
                    Annotations = Annotations.Empty
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

              match Diff.run (Some(Canon.render (JObj [ "kinds", JArr [] ]))) (art oneKind) (art after) with
              | Ok text ->
                  Expect.stringContains text "none declared (the manifest carries no `hosts` key)" "names the cause"
                  Expect.isFalse (text.Contains "codec: fuaran") "no default roster"
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
              match Diff.parse (art refIdl) with
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
              Expect.equal (severities cs) [ Diff.Additive ] "and it is additive" ]

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
    { FSharp = "System.DateOnly"
      Encode = "HostDate.encode"
      Decode = "HostDate.decode"
      Wire = None
      Format = None }

let private consequencesOf (before: Idl) (after: Idl) =
    diffOf before after |> List.collect Diff.consequences |> List.distinct

let private exitOf (before: Idl) (after: Idl) =
    match Diff.classifyDiff before after with
    | Ok v -> Diff.exitCode (Diff.verdictClass v)
    | Error e -> failtestf "classify: %s" e

[<Tests>]
let correctedRuleTests =
    testList
        "Phase 252 — int-to-float widening and the hosted codec"
        [ testCase "int to float is an additive widening, a construction break, exit 0" (fun _ ->
              Expect.equal (severities (diffOf (oneField TInt) (oneField TFloat))) [ Diff.Additive ] "additive"

              Expect.contains
                  (consequencesOf (oneField TInt) (oneField TFloat))
                  Diff.FullLiteralConstruction
                  "the generated field moved from int to float"

              Expect.equal (exitOf (oneField TInt) (oneField TFloat)) 0 "a gate absorbs it")

          testCase "the widening reaches a list element, and nothing else widens" (fun _ ->
              Expect.equal
                  (severities (diffOf (oneField (TList TInt)) (oneField (TList TFloat))))
                  [ Diff.Additive ]
                  "list<int> to list<float>"

              Expect.equal
                  (severities (diffOf (oneField TFloat) (oneField TInt)))
                  [ Diff.BreakingWire ]
                  "narrowing breaks"

              Expect.equal
                  (severities (diffOf (oneField TInt) (oneField TStr)))
                  [ Diff.BreakingWire ]
                  "a retype breaks"

              Expect.equal
                  (severities (diffOf (oneField (TList TInt)) (oneField (TMap TFloat))))
                  [ Diff.BreakingWire ]
                  "a widening inside a different container is not a widening")

          testCase "a hosted slot's codec swap is undecided, exit 4, and moves no generated shape" (fun _ ->
              let before = hostedField dateCodec

              let after =
                  hostedField
                      { dateCodec with
                          Encode = "HostDate.encodeEpochDays"
                          Decode = "HostDate.decodeEpochDays" }

              Expect.equal (severities (diffOf before after)) [ Diff.Unclassifiable ] "undecided"
              Expect.equal (exitOf before after) 4 "a gate stops"
              Expect.equal (consequencesOf before after) [ Diff.NoGeneratedShapeChange ] "the F# type did not move")

          testCase "a hosted slot's host-type change is undecided too, and a construction break" (fun _ ->
              let before = hostedField dateCodec

              let after =
                  hostedField
                      { dateCodec with
                          FSharp = "System.DateTime" }

              Expect.equal (severities (diffOf before after)) [ Diff.Unclassifiable ] "undecided"
              Expect.contains (consequencesOf before after) Diff.FullLiteralConstruction "the field's type moved")

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
                  [ Diff.HostSurfaceOnly ]
                  "host-surface"

              Expect.equal
                  (consequencesOf (fn "() => void") (fn "() => unknown"))
                  [ Diff.NoGeneratedShapeChange ]
                  "a TypeScript-only move leaves the F# shape where it was") ]

// ---------------------------------------------------------------------------
// Phase 293 — the descriptor table: the erased-slot rule walks nested slots, an authoring
// default's consequence follows the field's optionality, and the three verdict readers
// share one precedence.
// ---------------------------------------------------------------------------

let private verdictOf (before: Idl) (after: Idl) =
    match Diff.classifyDiff before after with
    | Ok v -> v
    | Error e -> failtestf "classify: %s" e

let private withDefaults (idl: Idl) (defaults: IdlDefault list) = { idl with Defaults = defaults }

[<Tests>]
let descriptorTableTests =
    testList
        "Phase 293 — the classifier's descriptor table"
        [ testCase "a type change across a NESTED erased slot is undecided, exit 4, and shape-unreadable" (fun _ ->
              // The rule used to test the top-level tag alone, so a list of hosted values
              // moving to a list of verbatim JSON reported `breaking-wire` where the document
              // said undecided — the artifact states nothing about what either slot admits.
              let before = oneField (TList(THosted dateCodec))
              let after = oneField (TList TJson)
              Expect.equal (severities (diffOf before after)) [ Diff.Unclassifiable ] "undecided, not breaking"
              Expect.equal (exitOf before after) 4 "exit 4"

              Expect.equal
                  (consequencesOf before after)
                  [ Diff.GeneratedShapeUnreadable ]
                  "and the F# axis is reported unreadable rather than guessed")

          testCase "a nested type change that crosses no erased slot is still decided" (fun _ ->
              let before = oneField (TList TStr)
              let after = oneField (TList TInt)

              Expect.equal
                  (severities (diffOf before after))
                  [ Diff.BreakingWire ]
                  "a value that decoded no longer does"

              Expect.equal (exitOf before after) 3 "exit 3")

          testCase "a map value and a union argument are walked too" (fun _ ->
              let before = oneField (TMap(THosted dateCodec))
              let after = oneField (TMap TOpaque)
              Expect.equal (severities (diffOf before after)) [ Diff.Unclassifiable ] "a map's value slot")

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
                      Diff.FullLiteralConstruction
                      "every `mkWeight` call site stops compiling: the parameter is gone"

                  Expect.contains
                      v.FSharpConsequences
                      Diff.StalePackageSlot
                      "a shape change carries the stale-slot consequence"

                  let removed = verdictOf after before

                  Expect.contains
                      removed.FSharpConsequences
                      Diff.FullLiteralConstruction
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
                  [ Diff.NoGeneratedShapeChange ]
                  "the constructor's body moves; its parameter list does not")

          testCase
              "without a snapshot to ask, the context-free `consequences` reads a default as a required field's"
              (fun _ ->
                  let c = Diff.classify (Diff.DefaultAdded("Weight", "grams", "0"))

                  Expect.equal
                      (Diff.consequences c)
                      [ Diff.FullLiteralConstruction; Diff.StalePackageSlot ]
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
                  Expect.equal (Diff.verdictClass v) Diff.VerdictClass.Undecided "undecided heads the class"

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
                                  CaseAnnotations = [] } ] }

                  let cs = diffOf before after |> List.map (fun c -> c.Change)

                  Expect.equal
                      cs
                      [ Diff.KindAdded "B"; Diff.EnumAdded "E" ]
                      "a kind (rank 10) sorts before an enum (rank 40), as the table ranks them") ]

// ---------------------------------------------------------------------------
// Phase 293 — support.json joins the classifier's inputs.
// ---------------------------------------------------------------------------

let private supportVerdict (before: SupportDocument option) (after: SupportDocument option) =
    match Diff.classifyDiffWith refIdl refIdl before after with
    | Ok v -> v
    | Error e -> failtestf "classifyDiffWith: %s" e

[<Tests>]
let supportInputTests =
    testList
        "Phase 293 — declared support is a classifier input"
        [ testCase
              "the same vocabulary with the same support is unchanged, and so is one with no support on either side"
              (fun _ ->
                  Expect.equal
                      (Diff.verdictClass (supportVerdict (Some support) (Some support)))
                      Diff.VerdictClass.Unchanged
                      "same support"

                  Expect.equal (Diff.verdictClass (supportVerdict None None)) Diff.VerdictClass.Unchanged "no support")

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

                  Expect.equal
                      (v.Changes |> List.map (fun c -> c.Severity))
                      [ Diff.HostSurfaceOnly ]
                      "host-surface, never wire"

                  Expect.equal (Diff.verdictClass v) Diff.VerdictClass.HostSurface "the class a gate reads"

                  Expect.contains
                      v.FSharpConsequences
                      Diff.FullLiteralConstruction
                      "a projection supplies the generated record, so a move there moves declarations"

                  match v.Changes.Head.Change with
                  | Diff.SupportChanged(key, Some _, Some _) ->
                      Expect.equal key "projection:Note" "keyed by the projected kind"
                  | other -> failtestf "expected a projection change, got %A" other)

          testCase "a case refine edited is host-surface with no generated shape change" (fun _ ->
              let edited =
                  { support with
                      Support =
                          { support.Support with
                              CaseRefines = support.Support.CaseRefines |> Map.map (fun _ e -> e + " (* refined *)") } }

              let v = supportVerdict (Some support) (Some edited)
              Expect.equal (v.Changes |> List.map (fun c -> c.Severity)) [ Diff.HostSurfaceOnly ] "host-surface"

              Expect.equal
                  v.FSharpConsequences
                  [ Diff.NoGeneratedShapeChange ]
                  "a decoder arm's final expression moves no declaration")

          testCase
              "support arriving where there was none reports every entry as added, and the artifact-only door still reads unchanged"
              (fun _ ->
                  let v = supportVerdict None (Some support)
                  Expect.isNonEmpty v.Changes "every declared entry is an added row"

                  for c in v.Changes do
                      match c.Change with
                      | Diff.SupportChanged(_, None, Some _) -> ()
                      | other -> failtestf "expected an added support entry, got %A" other

                  match Diff.classifyDiff refIdl refIdl with
                  | Ok plain ->
                      Expect.equal
                          (Diff.verdictClass plain)
                          Diff.VerdictClass.Unchanged
                          "the door with no support sees none"
                  | Error e -> failtestf "classifyDiff: %s" e)

          testCase "the mapping table carries the support row" (fun _ ->
              Expect.stringContains Diff.mappingTable "`SupportChanged`" "a row for the case") ]
