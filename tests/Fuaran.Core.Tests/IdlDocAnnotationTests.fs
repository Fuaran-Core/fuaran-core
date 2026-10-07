module Fuaran.Core.Tests.IdlDocAnnotationTests

open System
open System.IO
open Expecto
open Fuaran.Core.Idl

/// `Declare.enumAnnotate` over a well-formed fixture: its refusal (an error list since Phase 384)
/// is a defect of the suite.
let private annotate (annotations: (string * Annotations) list) (e: IdlEnum) : IdlEnum =
    match Declare.enumAnnotate annotations e with
    | Ok e -> e
    | Error errors -> failwithf "the fixture annotation was refused: %s" (String.concat "; " errors)

// ---------------------------------------------------------------------------
// Phase 255 — the authored `doc` annotation: what a member IS, declared once in
// the IDL, carried by the artifact and emitted by the F# generator as the
// member's `///` summary.
//
// Three claims, each asserted against a vocabulary rather than against prose:
//
//   1. **The artifact carries it verbatim** — an annotated declaration round-trips,
//      markup and line breaks included, and a vocabulary that declares no doc
//      renders exactly the bytes it did before the slot existed.
//   2. **The emission adds only the doc lines** — a vocabulary that gains a doc
//      regenerates with exactly the added `///` lines, ahead of the deprecation and
//      in-process notes, and nothing else moves.
//   3. **The emitted comment is valid XML whatever was authored, and reads as
//      authored.** The F# compiler reads a `///` block as TEXT (and encodes it
//      itself) unless its first line opens with `<`, when it reads it as XML; the
//      emitter follows that mode, so neither a raw `<'T>` in an XML block nor a
//      doubly-encoded one in a text block can occur. The committed
//      `DocAnnotatedGenerated.fs` is this file's vocabulary emitted — both modes —
//      and the test project compiles it with FS3390 as an ERROR, so an invalid
//      comment fails the build rather than printing a warning. The drift guard
//      below keeps that file what the generator emits.
// ---------------------------------------------------------------------------

let private f (name: string) (t: IdlType) (opt: Optionality) : IdlField =
    { Name = name
      Type = t
      Opt = opt
      Annotations = Annotations.Empty }

let private doc (text: string) =
    { Annotations.Empty with
        Doc = Some text }

let private withDoc (text: string) (fld: IdlField) : IdlField = { fld with Annotations = doc text }

/// Text a hostile or careless author could put in a doc: XML markup, a generic
/// type argument, an ampersand, a URL scheme the sanitisation floor rejects, both
/// line-break conventions, a blank line, a control character and an unpaired
/// surrogate. Every one must survive the artifact verbatim and reach the generated
/// source inside a comment, encoded.
let private hostileDoc =
    "Holds a List<'T> & a <b>bold</b> claim.\r\nSee javascript:alert(1) <script>x</script>.\n\nTab\there, bell \u0007, lone \uD800 end.\n"

/// A vocabulary documented on every annotatable declaration: a kind, a field of
/// a kind, a record field, two union cases (one also deprecated, in-process-only
/// and stamped) and an enum case through `Declare.enumAnnotate`.
let docIdl: Idl =
    { Kinds =
        [ { Tag = "Note"
            Category = "leaf"
            Annotations = doc "A short note.\nRenders as one paragraph & never wraps <'T>."
            Fields =
              [ f "label" TStr Required |> withDoc hostileDoc
                f "src" (TUnion("Src", [])) Optional
                f "tone" (TEnum "Tone") Required
                f "pair" (TRecord "Pair") Optional ] } ]
      Unions =
        [ { Name = "Src"
            Params = []
            Cases =
              [ { Tag = "Lit"
                  Fields = [ f "value" TStr Required ]
                  Annotations = doc "The literal case: the value is the text." }
                { Tag = "Ref"
                  Fields = [ f "target" TStr Required ]
                  Annotations =
                    { Deprecated =
                        Some
                            { Replacement = Some "Lit"
                              Message = Some "resolve the reference before encoding." }
                      InProcessOnly = true
                      Since = Some "0.2.0"
                      Doc = Some "A by-name reference to another note." } } ] } ]
      Enums =
        [ Declare.enumOf "Tone" [ "Quiet"; "Loud" ]
          // A doc that OPENS with `<` — the one shape the compiler reads as XML.
          |> annotate [ "Loud", doc "<'T> is not a tag here: Option<'T> & friends." ] ]
      Records =
        [ { Name = "Pair"
            Fields =
              [ f "left" TStr Required |> withDoc "The left half, < the right."
                f "right" TStr Required ] } ]
      Defaults = []
      NodeFields = []
      Ops = []
      Wire = WireShape.Default
      Harden = HardenPolicy.Undeclared }

/// The same vocabulary with every doc removed and every other annotation kept —
/// the BEFORE side of claim 2.
let private undocumented: Idl =
    let strip (a: Annotations) = { a with Doc = None }

    let stripField (x: IdlField) =
        { x with
            Annotations = strip x.Annotations }

    { docIdl with
        Kinds =
            docIdl.Kinds
            |> List.map (fun k ->
                { k with
                    Annotations = strip k.Annotations
                    Fields = k.Fields |> List.map stripField })
        Unions =
            docIdl.Unions
            |> List.map (fun u ->
                { u with
                    Cases =
                        u.Cases
                        |> List.map (fun c ->
                            { c with
                                Annotations = strip c.Annotations
                                Fields = c.Fields |> List.map stripField }) })
        Enums =
            docIdl.Enums
            |> List.map (fun e ->
                { e with
                    CaseAnnotations =
                        e.CaseAnnotations
                        |> List.map (fun (c, a) -> c, strip a)
                        |> List.filter (fun (_, a) -> not a.IsEmpty) })
        Records =
            docIdl.Records
            |> List.map (fun r ->
                { r with
                    Fields = r.Fields |> List.map stripField }) }

let private generatedModule = "Fuaran.Core.Tests.DocAnnotatedGenerated"

let private emit (idl: Idl) =
    match Gen.fsharpModule generatedModule idl [ "Note" ] with
    | Ok s -> s
    | Error e -> failtestf "codegen rejected the vocabulary: %A" e

let private lines (s: string) = s.Split('\n') |> Array.toList

/// The lines of `longer` that are not in `shorter`, provided `shorter` is
/// `longer` with only those lines deleted (an in-order subsequence); `None`
/// when anything else differs.
let private addedLines (shorter: string list) (longer: string list) : string list option =
    let rec go (s: string list) (l: string list) (acc: string list) =
        match s, l with
        | [], rest -> Some(List.rev acc @ rest)
        | _, [] -> None
        | x :: xs, y :: ys when x = y -> go xs ys acc
        | _, y :: ys -> go s ys (y :: acc)

    go shorter longer []

[<Tests>]
let tests =
    testList
        "IDL doc annotation (Phase 255)"
        [ testCase "an annotated declaration round-trips through the artifact, its text verbatim"
          <| fun _ ->
              let text = Artifact.render docIdl

              match Artifact.parse text with
              | Error e -> failtestf "the artifact did not read back: %s" e
              | Ok back ->
                  Expect.equal back (Artifact.canonicalise docIdl) "the read-back vocabulary is the declared one"

                  let label = back.Kinds.Head.Fields.Head
                  // Nothing on the way in or out strips, escapes or normalises the prose:
                  // markup, a rejected URL scheme, both line-break conventions and the
                  // control character are all still there.
                  Expect.equal
                      label.Annotations.Doc
                      (Some hostileDoc)
                      "the field's doc is byte-for-byte what was authored"

                  Expect.equal
                      ((back.Enums.Head.AnnotationsOf "Loud").Doc)
                      (Some "<'T> is not a tag here: Option<'T> & friends.")
                      "a doc declared through Declare.enumAnnotate rides the enum case"

          testCase "an absent doc writes no artifact key, and each declared doc writes exactly one"
          <| fun _ ->
              // The byte-for-byte half of this claim is held by the committed artifacts
              // and generated modules every other IDL family pins: none declares a doc,
              // and each still reproduces exactly. What is checked here is the shape —
              // an absent doc writes no key, and each declared doc writes exactly one.
              let docKeys (s: string) =
                  lines s
                  |> List.filter (fun l -> l.TrimStart().StartsWith "\"doc\": ")
                  |> List.length

              Expect.equal (docKeys (Artifact.render undocumented)) 0 "no doc key is written for an absent doc"
              Expect.equal (docKeys (Artifact.render docIdl)) 6 "one doc key per declared doc, and no other"

          testCase "a doc on one member regenerates with exactly the added /// lines and nothing else"
          <| fun _ ->
              let before = emit undocumented |> lines
              let after = emit docIdl |> lines

              match addedLines before after with
              | None ->
                  failtestf "the documented emission is not the undocumented one plus inserted lines:\n%s" (emit docIdl)
              | Some added ->
                  Expect.isNonEmpty added "the docs were emitted"

                  for l in added do
                      Expect.isTrue (l.TrimStart().StartsWith "///") (sprintf "an added line is a doc line: %s" l)

          testCase "the doc comes first, ahead of the deprecation, in-process and since notes"
          <| fun _ ->
              let out = emit docIdl |> lines

              let at (needle: string) =
                  out |> List.findIndex (fun l -> l.Contains needle)

              let docLine = at "A by-name reference to another note."
              Expect.isLessThan docLine (at "**Deprecated.**") "doc before the deprecation note"
              Expect.isLessThan (at "**Deprecated.**") (at "**In-process only**") "the Phase 113 order is unchanged"
              Expect.isLessThan (at "**In-process only**") (at "Since `0.2.0`") "the Phase 113 order is unchanged"

          testCase "a TEXT block: every authored line is its own /// line, verbatim, and none escapes the comment"
          <| fun _ ->
              let out = emit docIdl

              // Verbatim, because the compiler encodes a text block itself: encoding it
              // here too would show a reader `&lt;'T>`. Only the characters XML cannot
              // carry at all are replaced.
              let expected =
                  [ "      /// Holds a List<'T> & a <b>bold</b> claim."
                    "      /// See javascript:alert(1) <script>x</script>."
                    "      ///"
                    "      /// Tab\there, bell �, lone � end." ]
                  |> String.concat "\n"

              Expect.stringContains out (expected + "\n") "the field's doc block, line for line"

              // No authored fragment appears on a line that is not a comment.
              for fragment in [ "javascript:"; "bold"; "friends"; "Renders as one paragraph"; "the right" ] do
                  for l in lines out do
                      if l.Contains fragment then
                          Expect.isTrue
                              (l.TrimStart().StartsWith "///")
                              (sprintf "'%s' stays inside a comment: %s" fragment l)

              Expect.isFalse (out.Contains "\u0007") "no control character reaches the source"
              Expect.isFalse (out.Contains "&lt;'T> &amp; a") "a text block is not encoded twice"

          testCase "an XML block: a doc that opens with < becomes an explicit, encoded <summary>"
          <| fun _ ->
              let out = emit docIdl

              let expected =
                  [ "    /// <summary>"
                    "    /// &lt;'T> is not a tag here: Option&lt;'T> &amp; friends."
                    "    /// </summary>"
                    "    | Loud" ]
                  |> String.concat "\n"

              Expect.stringContains out expected "the enum case's doc, as XML the compiler accepts"

          testCase "an XML block carries the member's notes inside the summary, encoded"
          <| fun _ ->
              let xmlIdl =
                  { docIdl with
                      Enums =
                          [ Declare.enumOf "Tone" [ "Quiet"; "Loud" ]
                            |> annotate
                                [ "Loud",
                                  { Annotations.Empty with
                                      Doc = Some "  <b> leads, after spaces."
                                      Since = Some "1.0 <beta> & later" } ] ] }

              let expected =
                  [ "    /// <summary>"
                    "    ///   &lt;b> leads, after spaces."
                    "    /// Since `1.0 &lt;beta> &amp; later`."
                    "    /// </summary>"
                    "    | Loud" ]
                  |> String.concat "\n"

              Expect.stringContains (emit xmlIdl) expected "doc and note share one summary"

          testCase "the classifier grades a doc like any other annotation, with no change of its own"
          <| fun _ ->
              let classes (before: Idl) (after: Idl) =
                  let snap (idl: Idl) =
                      match Diff.parse (Artifact.render idl) with
                      | Ok s -> s
                      | Error e -> failtestf "snapshot: %s" e

                  Diff.changes (snap before) (snap after)
                  |> List.map (fun c -> (Diff.classify c).Severity)

              // `Lit` declared nothing before; `Ref` was already deprecated.
              let onlyLit =
                  { undocumented with
                      Unions =
                          [ { undocumented.Unions.Head with
                                Cases =
                                    undocumented.Unions.Head.Cases
                                    |> List.map (fun c ->
                                        if c.Tag = "Lit" then
                                            { c with
                                                Annotations = doc "The literal case." }
                                        else
                                            c) } ] }

              let onRef =
                  { undocumented with
                      Unions =
                          [ { undocumented.Unions.Head with
                                Cases =
                                    undocumented.Unions.Head.Cases
                                    |> List.map (fun c ->
                                        if c.Tag = "Ref" then
                                            { c with
                                                Annotations =
                                                    { c.Annotations with
                                                        Doc = Some "A reference." } }
                                        else
                                            c) } ] }

              Expect.equal (classes undocumented onlyLit) [ Diff.Additive ] "a doc on a bare member is additive"

              Expect.equal
                  (classes undocumented onRef)
                  [ Diff.HostSurfaceOnly ]
                  "a doc on an annotated member moves the host surface"

              Expect.equal
                  (classes onlyLit undocumented)
                  [ Diff.HostSurfaceOnly ]
                  "withdrawing a doc moves the host surface"

          testCase "a whitespace-only doc emits nothing, like an unsaid slot"
          <| fun _ ->
              let blank =
                  { undocumented with
                      Records =
                          [ { Name = "Pair"
                              Fields = [ f "left" TStr Required |> withDoc " \r\n\t "; f "right" TStr Required ] } ] }

              Expect.equal (emit blank) (emit undocumented) "a blank doc is byte-identical to none"

          testCase "the committed DocAnnotatedGenerated.fs is what the generator emits (compiled under FS3390-as-error)"
          <| fun _ ->
              let generated = emit docIdl
              let path = Snapshots.repoFile "tests/Fuaran.Core.Tests/DocAnnotatedGenerated.fs"

              if not (File.Exists path) then
                  failtestf "DocAnnotatedGenerated.fs not found at %s" path

              // Regeneration escape hatch, the IdlSpikeTests convention: FUARAN_REGEN=1
              // rewrites the committed file instead of asserting.
              Approval.write Approval.Regen Approval.Regenerated.DocAnnotated path generated
              |> ignore

              Expect.equal
                  generated
                  (File.ReadAllText path)
                  "the generator no longer reproduces DocAnnotatedGenerated.fs byte-for-byte - regenerate it with FUARAN_REGEN=1 and this test's filter" ]
