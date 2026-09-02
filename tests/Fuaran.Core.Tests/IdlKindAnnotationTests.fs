module Fuaran.Core.Tests.IdlKindAnnotationTests

open Expecto
open Fuaran.Core
open Fuaran.Core.Idl

// ---------------------------------------------------------------------------
// Phase 119 — declared annotations on a KIND (and therefore on a tree-op) and on
// an ENUM CASE, and the two-release retirement path they make affordable.
//
// Phase 113 put the bounded set on union cases and on fields and recorded, as a
// deliberate scope boundary, that `IdlKind` itself and `IdlEnum` cases were not
// annotatable. The consequence was that the vocabulary-growth charter admitted
// kinds and had no retirement path for one: a domain could not say a whole node
// kind was going away in a form that survived into the generated layer.
//
// The four helpers 113 shipped (`annotationsJson`, `annotationDocLines`,
// `obsoleteAttr`, `classifyAnnotations`) apply unchanged, so what is checked here
// is the two PLACEMENTS and the two new artifact positions — and the same two
// claims 113 made, because they are the ones that could go quietly wrong:
//
//   1. **The wire is untouched**, and an unannotated vocabulary's artifact and
//      emitted source are byte-for-byte what they were.
//   2. **Every surface renders it** — the artifact (and reads it back), both
//      source backends, and the stability classifier at the grades 113 declared:
//      MARKING is additive, moving or withdrawing a marking is host-surface only.
// ---------------------------------------------------------------------------

let private f (name: string) (t: IdlType) (opt: Optionality) : IdlField =
    { Name = name
      Type = t
      Opt = opt
      Annotations = Annotations.Empty }

let private deprecated (replacement: string option) (message: string option) =
    { Annotations.Empty with
        Deprecated =
            Some
                { Replacement = replacement
                  Message = message } }

let private since (v: string) =
    { Annotations.Empty with
        Since = Some v }

let private inProcessOnly =
    { Annotations.Empty with
        InProcessOnly = true }

/// A vocabulary with two kinds, an enum, a union and ONE op — nothing annotated.
/// The BEFORE side of every diff below, and the subject of the "absent is
/// omitted" assertions.
let private plainIdl: Idl =
    { Kinds =
        [ { Tag = "Legacy"
            Category = "leaf"
            Fields = [ f "text" TStr Required ]
            Annotations = Annotations.Empty }
          { Tag = "Note"
            Category = "leaf"
            Fields =
              [ f "text" TStr Required
                f "tone" (TEnum "Tone") Required
                f "src" (TUnion("Src", [])) Optional ]
            Annotations = Annotations.Empty } ]
      // A union, so the emitted spec records are `and`-joined members of the
      // type-recursion group — the placement a real vocabulary's kinds land in, and
      // the one the attribute has to ride the `and` for.
      Unions =
        [ { Name = "Src"
            Params = []
            Cases =
              [ { Tag = "Lit"
                  Fields = [ f "value" TStr Required ]
                  Annotations = Annotations.Empty } ] } ]
      Enums = [ Declare.enumOf "Tone" [ "Plain"; "Loud"; "Quiet" ] ]
      Records = []
      Defaults = []
      NodeFields = []
      Ops =
        [ { Tag = "Replace"
            Category = "op"
            Fields = [ f "target" TStr Required ]
            Annotations = Annotations.Empty } ]
      Wire = WireShape.Default
      Harden = HardenPolicy.Default }

/// The same vocabulary with the `Legacy` kind marked. Nothing about the SHAPE
/// differs, which is what makes the "the bytes did not move" assertions a real
/// comparison rather than a tautology.
let private withKindAnn (a: Annotations) : Idl =
    { plainIdl with
        Kinds =
            plainIdl.Kinds
            |> List.map (fun k -> if k.Tag = "Legacy" then { k with Annotations = a } else k) }

let private withOpAnn (a: Annotations) : Idl =
    { plainIdl with
        Ops = plainIdl.Ops |> List.map (fun o -> { o with Annotations = a }) }

/// The same vocabulary with the enum's `Quiet` case marked.
let private withEnumCaseAnn (a: Annotations) : Idl =
    { plainIdl with
        Enums = plainIdl.Enums |> List.map (Declare.enumAnnotate [ "Quiet", a ]) }

let private authored =
    VNode("n1", "Note", [ "text", VStr "x"; "tone", VEnum "Loud" ])

let private emitFs (idl: Idl) =
    match Gen.fsharpModule "KindAnn.Generated" idl [ "Legacy"; "Note" ] with
    | Ok s -> s
    | Error e -> failtestf "codegen rejected the vocabulary: %A" e

let private emitTs (idl: Idl) =
    Gen.typescriptModule idl [ "Legacy"; "Note" ]

let private snapshotOf (idl: Idl) =
    match Diff.parse (Artifact.render idl) with
    | Ok s -> s
    | Error e -> failtestf "snapshot: %s" e

let private classifyAll (before: Idl) (after: Idl) =
    Diff.changes (snapshotOf before) (snapshotOf after) |> List.map Diff.classify

/// The one classification whose change matches `pick`, or a failure naming what
/// was actually produced — an assertion that silently found nothing would pass
/// for the wrong reason.
let private theOne (pick: Diff.Change -> bool) (cs: Diff.Classification list) =
    match cs |> List.filter (fun c -> pick c.Change) with
    | [ one ] -> one
    | other ->
        failtestf "expected exactly one matching change; got %d of %A" (List.length other) (cs |> List.map _.Change)

[<Tests>]
let tests =
    testList
        "idl kind + enum-case annotations (Phase 119)"
        [

          // ---- 1. the model + the artifact ----------------------------------

          testCase "the codec never reads a kind or enum-case annotation — bytes are identical" (fun _ ->
              let enc (idl: Idl) =
                  match Encode.encode idl authored with
                  | Ok b -> b
                  | Error e -> failtestf "encode: %s" e

              let marked =
                  { withKindAnn (deprecated (Some "Note") (Some "folded into Note")) with
                      Enums = plainIdl.Enums |> List.map (Declare.enumAnnotate [ "Quiet", inProcessOnly ]) }

              Expect.equal (enc marked) (enc plainIdl) "an annotation moved a wire byte"

              // And decode is the encoder's inverse either side, so the annotation
              // has not merely been ignored on the way out.
              match Decode.decode marked (enc marked) with
              | Error e -> failtestf "decode: %s" e
              | Ok v -> Expect.equal (enc marked) (enc plainIdl) (sprintf "re-decoded %A" v))

          testCase "absent is the default and is OMITTED from the artifact" (fun _ ->
              let text = Artifact.render plainIdl

              Expect.isFalse
                  (text.Contains "annotations")
                  "an unannotated vocabulary must emit neither `annotations` nor `caseAnnotations` — that is
                   what keeps every pre-Phase-119 artifact byte-identical")

          testCase "a kind's own annotations render on the kind, beside its fields" (fun _ ->
              let text =
                  Artifact.render (withKindAnn (deprecated (Some "Note") (Some "use Note")))

              Expect.stringContains text "\"replacement\": \"Note\"" "the replacement is carried"
              Expect.stringContains text "\"message\": \"use Note\"" "the message is carried"

              // The `annotations` key sits on the KIND object, not inside its
              // `fields` array — the kind says this about itself.
              match Artifact.parse text with
              | Error e -> failtestf "the annotated artifact did not read back: %s" e
              | Ok back ->
                  let k = back.Kinds |> List.find (fun k -> k.Tag = "Legacy")

                  Expect.equal
                      k.Annotations.Deprecated
                      (Some
                          { Replacement = Some "Note"
                            Message = Some "use Note" })
                      "the kind's own set round-trips"

                  Expect.isTrue
                      (k.Fields |> List.forall (fun fld -> fld.Annotations.IsEmpty))
                      "and none of it leaked onto a field")

          testCase "an OP is annotatable on identical terms — it is an IdlKind" (fun _ ->
              let text = Artifact.render (withOpAnn (since "0.18.0"))

              match Artifact.parse text with
              | Error e -> failtestf "did not read back: %s" e
              | Ok back ->
                  Expect.equal
                      (back.Ops |> List.exactlyOne).Annotations.Since
                      (Some "0.18.0")
                      "the op's set round-trips")

          testCase "enum-case annotations render keyed by the WIRE string, and read back by host name" (fun _ ->
              // The mapped enum is the case worth pinning: the artifact keys on the
              // wire string (which `cases` always carries), while the model keys on
              // the HOST case name (which is what the F# backend attaches to).
              let mapped =
                  Declare.enumWith "Tone" [ "Plain", "plain"; "Loud", "loud"; "Quiet", "quiet" ]
                  |> Declare.enumAnnotate [ "Quiet", deprecated (Some "Plain") None ]

              let idl = { plainIdl with Enums = [ mapped ] }
              let text = Artifact.render idl

              Expect.stringContains text "\"caseAnnotations\"" "the key is emitted"
              Expect.stringContains text "\"quiet\"" "keyed by the wire string"

              match Artifact.parse text with
              | Error e -> failtestf "did not read back: %s" e
              | Ok back ->
                  let e = back.Enums |> List.exactlyOne

                  Expect.equal
                      (e.AnnotationsOf "Quiet").Deprecated
                      (Some
                          { Replacement = Some "Plain"
                            Message = None })
                      "the host case name carries it back"

                  Expect.isTrue (e.AnnotationsOf "Plain").IsEmpty "and an unannotated case says nothing")

          testCase "the artifact round-trip law holds with both placements annotated" (fun _ ->
              let idl =
                  { withKindAnn (since "0.18.0") with
                      Enums = plainIdl.Enums |> List.map (Declare.enumAnnotate [ "Quiet", inProcessOnly ]) }

              match Artifact.parse (Artifact.render idl) with
              | Error e -> failtestf "did not read back: %s" e
              | Ok back -> Expect.equal back (Artifact.canonicalise idl) "parse (render idl) = canonicalise idl")

          testCase "an artifact naming a case the enum does not declare is REFUSED, not dropped" (fun _ ->
              let text =
                  (Artifact.render (withEnumCaseAnn (since "0.18.0"))).Replace("\"Quiet\": {", "\"Screaming\": {")

              match Artifact.parse text with
              | Ok _ -> failtest "a dangling case annotation must not read back silently"
              | Error e -> Expect.stringContains e "Screaming" "and the refusal names it")

          // ---- 2. declaration-site well-formedness --------------------------

          testCase "Declare.enumAnnotate refuses a case the enum does not declare" (fun _ ->
              Expect.throws
                  (fun () ->
                      Declare.enumAnnotate [ "Screaming", since "0.18.0" ] (Declare.enumOf "Tone" [ "Plain" ])
                      |> ignore)
                  "an annotation on nothing is a typo, not a declaration")

          testCase "enumWireErrors is the backstop for a record built by literal" (fun _ ->
              let broken =
                  { plainIdl with
                      Enums =
                          [ { Name = "Tone"
                              Cases = [ "Plain" ]
                              Wires = []
                              CaseAnnotations = [ "Screaming", since "0.18.0" ] } ] }

              let errs = Declare.enumWireErrors broken
              Expect.hasLength errs 1 "one finding"
              Expect.stringContains errs[0] "does not declare" "naming the class")

          // ---- 3. the F# backend --------------------------------------------

          testCase "F#: a deprecated KIND emits a doc block and ONE warning-grade Obsolete" (fun _ ->
              let src = emitFs (withKindAnn (deprecated (Some "Note") (Some "folded in")))

              Expect.stringContains src "/// **Deprecated.** Use `Note` instead." "the doc block names the replacement"
              Expect.stringContains src "/// folded in" "the doc block carries the message"

              // The spec records are `and`-joined members of one type-recursion
              // group, so the attribute sits INLINE after the `and` — which is the
              // position `[<RequireQualifiedAccess>]` already occupies there.
              Expect.stringContains
                  src
                  "and [<System.Obsolete(\"deprecated — use `Note` instead: folded in\", false)>] LegacySpec"
                  "the attribute rides the `and`, composed into one message, isError = false"

              Expect.equal
                  (src.Split("System.Obsolete").Length - 1)
                  1
                  "exactly one Obsolete — the attribute is not AllowMultiple, so two would not compile"

              Expect.stringContains
                  src
                  "#nowarn \"44\""
                  "and the layer suppresses FS0044 for itself — it constructs and matches every declared kind")

          testCase "F#: a deprecated ENUM CASE takes the union-case placement" (fun _ ->
              let src = emitFs (withEnumCaseAnn (deprecated (Some "Plain") None))

              Expect.stringContains src "/// **Deprecated.** Use `Plain` instead." "the doc block sits above the bar"

              Expect.stringContains
                  src
                  "| [<System.Obsolete(\"deprecated — use `Plain` instead\", false)>] Quiet"
                  "and the attribute inline after it, which is where F# accepts one on a DU case"

              Expect.stringContains src "    | Plain" "an unannotated case emits the line it always did")

          testCase "F#: `since` on a kind is doc-only, and earns no suppression" (fun _ ->
              let src = emitFs (withKindAnn (since "0.18.0"))

              Expect.stringContains src "/// Since `0.18.0`." "documented"
              Expect.isFalse (src.Contains "System.Obsolete") "a `since` earns no attribute"
              Expect.isFalse (src.Contains "#nowarn \"44\"") "and no suppression, because nothing warns")

          testCase "F#: the attribute leads with `type` when the kind is the group's FIRST member" (fun _ ->
              // A union-free vocabulary puts a spec record at the head of the
              // type-recursion group, where an attribute sits on its own preceding
              // line instead of riding an `and`. Both positions are emitted by one
              // rule, so both are pinned.
              let unionFree =
                  { plainIdl with
                      Unions = []
                      Kinds =
                          plainIdl.Kinds
                          |> List.map (fun k ->
                              { k with
                                  Fields = k.Fields |> List.filter (fun fld -> fld.Name <> "src")
                                  Annotations =
                                      if k.Tag = "Legacy" then
                                          deprecated None None
                                      else
                                          k.Annotations }) }

              Expect.stringContains
                  (emitFs unionFree)
                  "[<System.Obsolete(\"deprecated\", false)>]\ntype LegacySpec"
                  "on its own line above `type`")

          testCase "F#: an unannotated vocabulary's emitted source is unchanged" (fun _ ->
              let bare = emitFs plainIdl
              Expect.isFalse (bare.Contains "System.Obsolete") "nothing to say"
              Expect.isFalse (bare.Contains "**Deprecated.**") "and no annotation doc block appears"
              Expect.isFalse (bare.Contains "/// Since") "nor a since line"
              Expect.isFalse (bare.Contains "#nowarn") "nor the suppression")

          // ---- 4. the TypeScript backend ------------------------------------

          testCase "TS: a kind's marking is named at its encoder AND its decoder" (fun _ ->
              let src = emitTs (withKindAnn (deprecated (Some "Note") None))

              let hits =
                  src.Split('\n')
                  |> Array.filter (fun l -> l.Contains "@deprecated `Legacy`")
                  |> Array.length

              Expect.equal hits 2 "the two generated functions that ARE the kind on this side"
              Expect.stringContains src "use `Note` instead" "and it names the replacement"

              Expect.isFalse
                  (src.Contains "/**")
                  "line comments, not JSDoc — a `@deprecated` block above `encLegacySpec` would tell tooling the
                   ENCODER is deprecated, which is false")

          testCase "TS: an enum case's marking is named at the enum's decoder" (fun _ ->
              let src = emitTs (withEnumCaseAnn (deprecated None (Some "spell it Plain")))

              Expect.stringContains src "@deprecated `Tone.\"Quiet\"`" "named by enum and wire string"
              Expect.stringContains src "spell it Plain" "and the message is carried")

          testCase "TS: an unannotated vocabulary's emitted JS is unchanged" (fun _ ->
              let bare = emitTs plainIdl
              Expect.isFalse (bare.Contains "@deprecated") "nothing to say"
              Expect.isFalse (bare.Contains "in-process only") "nothing to say")

          // ---- 5. the stability classifier ----------------------------------

          testCase "diff: MARKING a kind is additive" (fun _ ->
              let c =
                  classifyAll plainIdl (withKindAnn (deprecated (Some "Note") None))
                  |> theOne (function
                      | Diff.KindAnnotationsChanged(Diff.OKind "Legacy", _, _) -> true
                      | _ -> false)

              Expect.equal
                  c.Severity
                  Diff.Additive
                  "marking a whole kind breaks nothing — that is what makes the charter's two-release
                   retirement affordable"

              Expect.stringContains c.Rationale "never on the wire" "and the rationale says why")

          testCase "diff: WITHDRAWING a kind's marking is a plain host-surface change" (fun _ ->
              let c =
                  classifyAll (withKindAnn (deprecated (Some "Note") None)) plainIdl
                  |> theOne (function
                      | Diff.KindAnnotationsChanged _ -> true
                      | _ -> false)

              Expect.equal c.Severity Diff.HostSurfaceOnly "un-retiring a kind is not a breaking event"
              Expect.stringContains c.Rationale "WITHDRAWN" "and the rationale says which direction it moved")

          testCase "diff: an OP's marking grades the same, and describes itself as an op" (fun _ ->
              let c =
                  classifyAll plainIdl (withOpAnn (deprecated None None))
                  |> theOne (function
                      | Diff.KindAnnotationsChanged(Diff.OOp "Replace", _, _) -> true
                      | _ -> false)

              Expect.equal c.Severity Diff.Additive "marking an op breaks nothing either"
              Expect.stringContains c.Rationale "op Replace" "and it is described as an op, not as a kind")

          testCase "diff: MARKING an enum case is additive; withdrawing it is host-surface" (fun _ ->
              let marked = withEnumCaseAnn (deprecated (Some "Plain") None)

              let gained =
                  classifyAll plainIdl marked
                  |> theOne (function
                      | Diff.EnumCaseAnnotationsChanged("Tone", "Quiet", _, _) -> true
                      | _ -> false)

              Expect.equal gained.Severity Diff.Additive "marking an enum case breaks nothing"

              let withdrawn =
                  classifyAll marked plainIdl
                  |> theOne (function
                      | Diff.EnumCaseAnnotationsChanged _ -> true
                      | _ -> false)

              Expect.equal withdrawn.Severity Diff.HostSurfaceOnly "and withdrawing it is a plain change")

          testCase "diff: a marking reports NOTHING about the shape" (fun _ ->
              // The failure this guards is the classifier pricing a retirement
              // marking as a kind add/remove or a wire event, which would make the
              // whole mechanism unusable.
              let cs =
                  classifyAll
                      plainIdl
                      { withKindAnn inProcessOnly with
                          Enums = plainIdl.Enums |> List.map (Declare.enumAnnotate [ "Quiet", since "0.18.0" ]) }

              Expect.isFalse
                  (cs
                   |> List.exists (fun c -> c.Severity = Diff.BreakingWire || c.Severity = Diff.BreakingForEmitters))
                  "no annotation change may ever grade as breaking"

              Expect.isFalse
                  (cs
                   |> List.exists (fun c ->
                       match c.Change with
                       | Diff.KindAdded _
                       | Diff.KindRemoved _
                       | Diff.EnumCaseAdded _
                       | Diff.EnumCaseRemoved _
                       | Diff.KindCategoryChanged _ -> true
                       | _ -> false))
                  "and none of it may be reported as the kind or the case itself moving")

          // ---- 6. the retirement path, end to end ---------------------------

          testCase "the two-release retirement: mark, then remove" (fun _ ->
              // Release 1 marks the kind. Nothing breaks — a consumer sees a warning.
              let marked = withKindAnn (deprecated (Some "Note") (Some "gone next release"))

              let markGrades = classifyAll plainIdl marked |> List.map _.Severity

              Expect.isTrue
                  (markGrades |> List.forall (fun s -> s = Diff.Additive))
                  "release 1 costs nothing — the marking alone is additive"

              // Release 2 removes it. THAT is the breaking event, and it is priced
              // where it belongs rather than at the marking.
              let removed =
                  { marked with
                      Kinds = marked.Kinds |> List.filter (fun k -> k.Tag <> "Legacy") }

              let c =
                  classifyAll marked removed
                  |> theOne (function
                      | Diff.KindRemoved "Legacy" -> true
                      | _ -> false)

              Expect.notEqual c.Severity Diff.Additive "removing the kind is where the cost lands") ]
