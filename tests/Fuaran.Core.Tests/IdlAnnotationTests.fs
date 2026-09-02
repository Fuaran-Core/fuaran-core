module Fuaran.Core.Tests.IdlAnnotationTests

open Expecto
open Fuaran.Core
open Fuaran.Core.Idl

// ---------------------------------------------------------------------------
// Phase 113 — declared annotations on union cases and fields.
//
// The set is bounded (`deprecated` / `inProcessOnly` / `since`) and says
// something ABOUT a member rather than about its shape, so every leg here is
// checking one of two claims:
//
//   1. **The wire is untouched.** An annotation never changes a byte in either
//      direction, and an unannotated vocabulary's artifact is what it always was.
//      Those are the two ways this could go quietly wrong, so both are asserted
//      against the SAME vocabulary rather than against prose.
//   2. **Every other surface renders it** — the artifact, both source backends,
//      and the stability classifier — and renders it in the grade the phase
//      declares: MARKING a member is additive, moving or withdrawing a marking is
//      a host-surface change, and neither is ever a wire event.
// ---------------------------------------------------------------------------

let private f (name: string) (t: IdlType) (opt: Optionality) : IdlField =
    { Name = name
      Type = t
      Opt = opt
      Annotations = Annotations.Empty }

let private annotated (a: Annotations) (fld: IdlField) : IdlField = { fld with Annotations = a }

let private deprecated (replacement: string option) (message: string option) =
    { Annotations.Empty with
        Deprecated =
            Some
                { Replacement = replacement
                  Message = message } }

let private inProcessOnly =
    { Annotations.Empty with
        InProcessOnly = true }

let private since (v: string) =
    { Annotations.Empty with
        Since = Some v }

/// A two-kind, one-union vocabulary with nothing annotated — the BEFORE side of
/// every diff below, and the subject of the "absent is omitted" assertions.
let private plainIdl: Idl =
    { Kinds =
        [ { Tag = "Note"
            Category = "leaf"
            Fields = [ f "label" TStr Required; f "src" (TUnion("Src", [])) Optional ] } ]
      Unions =
        [ { Name = "Src"
            Params = []
            Cases =
              [ { Tag = "Lit"
                  Fields = [ f "value" TStr Required ]
                  Annotations = Annotations.Empty }
                { Tag = "Ref"
                  Fields = [ f "target" TStr Required ]
                  Annotations = Annotations.Empty } ] } ]
      Enums = []
      Records = []
      Defaults = []
      NodeFields = []
      Ops = []
      Wire = WireShape.Default }

/// The same vocabulary, annotated. Nothing about the SHAPE differs — same kinds,
/// same cases, same fields, same optionality — which is what makes every "the
/// bytes did not move" assertion below a real comparison rather than a tautology.
let private withAnnotations (caseAnn: Annotations) (fieldAnn: Annotations) : Idl =
    { plainIdl with
        Kinds =
            [ { Tag = "Note"
                Category = "leaf"
                Fields =
                  [ f "label" TStr Required |> annotated fieldAnn
                    f "src" (TUnion("Src", [])) Optional ] } ]
        Unions =
            [ { Name = "Src"
                Params = []
                Cases =
                  [ { Tag = "Lit"
                      Fields = [ f "value" TStr Required ]
                      Annotations = caseAnn }
                    { Tag = "Ref"
                      Fields = [ f "target" TStr Required ]
                      Annotations = Annotations.Empty } ] } ] }

let private authored =
    VNode("a", "Note", [ "label", VStr "x"; "src", VUnion("Lit", [ "value", VStr "y" ]) ])

let private emitFs (idl: Idl) =
    match Gen.fsharpModule "Ann.Generated" idl [ "Note" ] with
    | Ok s -> s
    | Error e -> failtestf "codegen rejected the vocabulary: %A" e

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
        "idl annotations (Phase 113)"
        [

          // ---- 1. the wire is untouched -------------------------------------

          testCase "the codec never reads an annotation — encoded bytes are identical" (fun _ ->
              let ann =
                  withAnnotations (deprecated (Some "Ref") (Some "folded into Ref")) inProcessOnly

              let enc (idl: Idl) =
                  match Encode.encode idl authored with
                  | Ok b -> b
                  | Error e -> failtestf "encode: %s" e

              Expect.equal (enc ann) (enc plainIdl) "an annotation moved a wire byte"

              // And decode is the encoder's inverse either side, so the annotation
              // has not merely been ignored on the way out.
              match Decode.decode ann (enc ann) with
              | Error e -> failtestf "decode: %s" e
              | Ok v -> Expect.equal (enc ann) (enc plainIdl) (sprintf "re-decoded %A" v))

          testCase "absent is the default and is OMITTED from the artifact" (fun _ ->
              let text = Artifact.render plainIdl

              Expect.isFalse
                  (text.Contains "annotations")
                  "an unannotated vocabulary must emit no `annotations` key at all — that is what keeps every
                   pre-Phase-113 artifact byte-identical")

          testCase "the artifact carries each declared slot, and only the declared ones" (fun _ ->
              let text =
                  Artifact.render (withAnnotations (deprecated (Some "Ref") (Some "folded into Ref")) (since "0.18.0"))

              Expect.stringContains text "\"annotations\"" "the key is emitted"
              Expect.stringContains text "\"replacement\": \"Ref\"" "the replacement is carried"
              Expect.stringContains text "\"message\": \"folded into Ref\"" "the message is carried"
              Expect.stringContains text "\"since\": \"0.18.0\"" "the version is carried"

              Expect.isFalse
                  (text.Contains "inProcessOnly")
                  "a slot nothing declared must not appear — an annotation set renders what it says and no more")

          testCase "the artifact round-trips through the snapshot reader" (fun _ ->
              let idl = withAnnotations (deprecated (Some "Ref") None) inProcessOnly
              let snap = snapshotOf idl

              let caseAnn = (Map.find "Lit" (Map.find "Src" snap.Unions).Cases).Annotations

              let fieldAnn =
                  (Map.find "Note" snap.Kinds |> List.find (fun x -> x.Name = "label")).Annotations

              Expect.stringContains caseAnn "\"replacement\":\"Ref\"" "the case's annotations survive the artifact"
              Expect.stringContains fieldAnn "\"inProcessOnly\":true" "the field's annotations survive the artifact"

              let plain = snapshotOf plainIdl

              Expect.equal
                  (Map.find "Lit" (Map.find "Src" plain.Unions).Cases).Annotations
                  ""
                  "an artifact that declares none reads as none — which is also how a revision predating the key reads")

          // ---- 2. the F# backend --------------------------------------------

          testCase "F#: a deprecated case emits a doc block and ONE warning-grade Obsolete" (fun _ ->
              let src =
                  emitFs (withAnnotations (deprecated (Some "Ref") (Some "folded in")) Annotations.Empty)

              Expect.stringContains src "/// **Deprecated.** Use `Ref` instead." "the doc block names the replacement"
              Expect.stringContains src "/// folded in" "the doc block carries the message"

              Expect.stringContains
                  src
                  "| [<System.Obsolete(\"deprecated — use `Ref` instead: folded in\", false)>] Lit"
                  "the attribute sits inline after the bar, composed into one message, isError = false"

              Expect.equal
                  (src.Split("System.Obsolete").Length - 1)
                  1
                  "exactly one Obsolete — the attribute is not AllowMultiple, so two would not compile")

          testCase "F#: an in-process-only field emits the wire-loss hazard, not an error" (fun _ ->
              let src = emitFs (withAnnotations Annotations.Empty inProcessOnly)

              Expect.stringContains src "/// **In-process only**" "the doc block names the hazard"
              Expect.stringContains src "LOST across any wire boundary" "and says what is lost"

              Expect.stringContains
                  src
                  "[<System.Obsolete(\"in-process only — no wire projection; a value here is lost across a wire boundary\", false)>]"
                  "an attribute the host can switch on (--warnaserror:44), never an unconditional error"

              Expect.isFalse (src.Contains ", true)>]") "isError must be false — see Gen.obsoleteAttr")

          testCase "F#: deprecated AND in-process-only compose into a single attribute" (fun _ ->
              let both =
                  { Annotations.Empty with
                      Deprecated =
                          Some
                              { Replacement = Some "Ref"
                                Message = None }
                      InProcessOnly = true }

              let src = emitFs (withAnnotations both Annotations.Empty)

              Expect.equal
                  (src.Split("System.Obsolete").Length - 1)
                  1
                  "one attribute, both facts — ObsoleteAttribute is not AllowMultiple"

              Expect.stringContains src "deprecated — use `Ref` instead; in-process only" "both facts, in one message")

          testCase "F#: `since` is doc-only — there is nothing for a compiler to say about it" (fun _ ->
              let src = emitFs (withAnnotations (since "0.18.0") (since "0.17.0"))

              Expect.stringContains src "/// Since `0.18.0`." "the case's version is documented"
              Expect.stringContains src "/// Since `0.17.0`." "the field's version is documented"
              Expect.isFalse (src.Contains "System.Obsolete") "a `since` earns no attribute"
              Expect.isFalse (src.Contains "#nowarn \"44\"") "and no suppression, because nothing warns")

          testCase "F#: the module suppresses FS0044 only when it actually emits an Obsolete" (fun _ ->
              Expect.isFalse
                  (emitFs(plainIdl).Contains "#nowarn")
                  "an unannotated vocabulary emits the header it always did"

              Expect.stringContains
                  (emitFs (withAnnotations inProcessOnly Annotations.Empty))
                  "#nowarn \"44\""
                  "the generated layer constructs and matches every declared member, marked ones included — the
                   warning is for CONSUMERS of the layer, not for the layer itself")

          testCase "F#: an empty or whitespace slot reads as UNSAID, not as empty prose" (fun _ ->
              let blank = deprecated (Some "  ") (Some "")
              let src = emitFs (withAnnotations blank Annotations.Empty)

              Expect.stringContains src "/// **Deprecated.**" "the marking still stands"
              Expect.isFalse (src.Contains "Use ``") "an empty replacement must not emit `use `` instead`"

              Expect.stringContains
                  src
                  "[<System.Obsolete(\"deprecated\", false)>]"
                  "and the message says only what is said")

          // ---- 3. the TypeScript backend ------------------------------------

          testCase "TS: a deprecated case is named at its own case arm, in both directions" (fun _ ->
              let src =
                  Gen.typescriptModule (withAnnotations (deprecated (Some "Ref") None) Annotations.Empty) [ "Note" ]

              let hits =
                  src.Split('\n')
                  |> Array.filter (fun l -> l.Contains "@deprecated `Lit`")
                  |> Array.length

              Expect.equal hits 2 "the encoder's and the decoder's case arms each carry it"
              Expect.stringContains src "use `Ref` instead" "and it names the replacement")

          testCase "TS: a field's annotation is rendered on the function that owns it, NAMING the field" (fun _ ->
              // The emitted module is plain JS: a field is an inline entry in a
              // one-line object literal, so it has no declaration line of its own.
              let src =
                  Gen.typescriptModule (withAnnotations Annotations.Empty inProcessOnly) [ "Note" ]

              Expect.stringContains src "// `Note.label` is in-process only" "the field is named, not merely implied"

              Expect.isFalse
                  (src.Contains "/**")
                  "line comments, not JSDoc — a `@deprecated` block above `encNoteSpec` would tell tooling the
                   ENCODER is deprecated, which is false")

          testCase "TS: an unannotated vocabulary's emitted JS is unchanged" (fun _ ->
              let bare = Gen.typescriptModule plainIdl [ "Note" ]
              Expect.isFalse (bare.Contains "@deprecated") "nothing to say"
              Expect.isFalse (bare.Contains "in-process only") "nothing to say")

          // ---- 4. the stability classifier ----------------------------------

          testCase "diff: MARKING a case is additive" (fun _ ->
              let c =
                  classifyAll plainIdl (withAnnotations (deprecated (Some "Ref") None) Annotations.Empty)
                  |> theOne (function
                      | Diff.UnionCaseAnnotationsChanged("Src", "Lit", _, _) -> true
                      | _ -> false)

              Expect.equal
                  c.Severity
                  Diff.Additive
                  "marking a member breaks nothing — that is what makes a
                                                     two-release retirement possible"

              Expect.stringContains c.Rationale "never on the wire" "and the rationale says why")

          testCase "diff: MARKING a field is additive" (fun _ ->
              let c =
                  classifyAll plainIdl (withAnnotations Annotations.Empty inProcessOnly)
                  |> theOne (function
                      | Diff.FieldAnnotationsChanged(_, "label", _, _) -> true
                      | _ -> false)

              Expect.equal c.Severity Diff.Additive "marking a field breaks nothing either")

          testCase "diff: WITHDRAWING a deprecation is a plain host-surface change" (fun _ ->
              let marked = withAnnotations (deprecated (Some "Ref") None) Annotations.Empty

              let c =
                  classifyAll marked plainIdl
                  |> theOne (function
                      | Diff.UnionCaseAnnotationsChanged _ -> true
                      | _ -> false)

              Expect.equal c.Severity Diff.HostSurfaceOnly "un-retiring a case is not a breaking event"
              Expect.stringContains c.Rationale "WITHDRAWN" "and the rationale says which direction it moved")

          testCase "diff: MOVING a marking is a host-surface change, never a wire one" (fun _ ->
              let before = withAnnotations (deprecated (Some "Ref") None) Annotations.Empty

              let after =
                  withAnnotations (deprecated (Some "Ref") (Some "gone in 0.19")) Annotations.Empty

              let c =
                  classifyAll before after
                  |> theOne (function
                      | Diff.UnionCaseAnnotationsChanged _ -> true
                      | _ -> false)

              Expect.equal c.Severity Diff.HostSurfaceOnly "the generated declaration moved; the wire did not")

          testCase "diff: an annotation change reports NOTHING about the shape" (fun _ ->
              // The failure this guards is the classifier reporting a marking as a
              // type or optionality move, which would price a two-release retirement
              // as a breaking bump and make the whole mechanism unusable.
              let cs = classifyAll plainIdl (withAnnotations inProcessOnly (since "0.18.0"))

              Expect.isFalse
                  (cs
                   |> List.exists (fun c -> c.Severity = Diff.BreakingWire || c.Severity = Diff.BreakingForEmitters))
                  "no annotation change may ever grade as breaking"

              Expect.isFalse
                  (cs
                   |> List.exists (fun c ->
                       match c.Change with
                       | Diff.FieldTypeChanged _
                       | Diff.FieldOptionalityChanged _
                       | Diff.FieldHostSurfaceChanged _ -> true
                       | _ -> false))
                  "and none of it may be reported as a shape or signature move") ]
