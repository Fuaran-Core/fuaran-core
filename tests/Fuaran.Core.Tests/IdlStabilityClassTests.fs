module Fuaran.Core.Tests.IdlStabilityClassTests

open System
open System.Diagnostics
open System.IO
open Expecto
open Fuaran.Core
open Fuaran.Core.Idl

// ---------------------------------------------------------------------------
// Phase 127 — the one classifier entry point, the F# consequence table, and the
// wire-profile bump.
//
// `IdlDiffTests.fs` beside this file pins the WIRE severities (Phase 700). This
// file pins the three things added on top, and the ones worth reading are the ones
// where the two axes DISAGREE — because a caller that reads only the wire verdict
// gets those wrong, and they are the whole reason the second axis exists:
//
//   - an OPTIONAL field added is `Additive` on the wire and still breaks every
//     full record literal (`FS0764`);
//   - a `hostSurface` edit is invisible on the wire and moves a generated field's
//     type;
//   - an UNDECIDED row yields no profile at all, rather than the minor the
//     decidable remainder would suggest.
//
// **The compile claims are COMPILED, not asserted.** Two legs spawn `dotnet fsi`
// over the real generator output and read the real compiler's verdict, because the
// claim is about F#'s behaviour and a test that merely restated the mapping would
// certify this file against itself. Each runs in both directions — the perturbed
// consumer must fail AND the adapted consumer must pass — so a leg that has
// stopped measuring anything is visible rather than green.
//
// The property leg is the cheap generalisation of those two: over every
// perturbation in the declared set, the consequence the classifier reports must
// match what the generator's OUTPUT structurally exhibits. Its falsifier is stated
// at the test.
// ---------------------------------------------------------------------------

module private Fixtures =

    let ann = Annotations.Empty

    let fld name ty opt : IdlField =
        { Name = name
          Type = ty
          Opt = opt
          Annotations = ann }

    let kindOf tag fields : IdlKind =
        { Tag = tag
          Category = "display"
          Fields = fields
          Annotations = ann }

    let case tag fields : IdlUnionCase =
        { Tag = tag
          Fields = fields
          Annotations = ann }

    /// The shared revision. Deliberately small and deliberately scalar-typed: the
    /// compile legs below feed `Gen.fsharpTypes`' output straight to `fsi`, so a
    /// field type that pulled in the node tree or a hosted codec would make those
    /// legs measure the reference closure rather than the shape change.
    let baseIdl: Idl =
        { Kinds = [ kindOf "Note" [ fld "body" TStr Required; fld "loudness" (TEnum "Loudness") Optional ] ]
          Unions =
            [ { Name = "Source"
                Params = []
                Cases =
                  [ case "Inline" [ fld "text" TStr Required ]
                    case "Ref" [ fld "id" TStr Required ] ] } ]
          Enums =
            [ { Name = "Loudness"
                Cases = [ "Plain"; "Loud" ]
                Wires = []
                CaseAnnotations = [] } ]
          Records = []
          Defaults = []
          NodeFields = []
          Ops = []
          Wire = WireShape.Default
          Harden = HardenPolicy.Default }

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

let private verdict (before: Idl) (after: Idl) : Diff.Verdict =
    match Diff.classifyDiff before after with
    | Ok v -> v
    | Error e -> failtestf "classifyDiff did not read its own artifact back: %s" e

let private consequencesOf (before: Idl) (after: Idl) =
    (verdict before after).FSharpConsequences

let private classOf (before: Idl) (after: Idl) =
    Diff.verdictClass (verdict before after) |> Diff.classLabel

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

/// `tests/Fuaran.Core.Tests/fixtures/idl-classify/<name>.json`, resolved from the
/// repository root so the same three files serve the in-process legs and the
/// command legs.
let fixtureFiles: (string * Idl) list =
    [ "before", Fixtures.baseIdl
      "after-required-field", Fixtures.requiredFieldAdded
      "after-optional-field", Fixtures.optionalFieldAdded ]

let fixtureDir = "tests/Fuaran.Core.Tests/fixtures/idl-classify"

let private fixturePath (name: string) =
    Snapshots.repoFile (fixtureDir + "/" + name + ".json")

/// Rewrite the committed fixtures from the declarations above — the
/// `--regen-idl-classify-fixtures` entry point.
let regen () =
    let dir = Path.GetDirectoryName(fixturePath "before")
    Directory.CreateDirectory dir |> ignore

    for (name, idl) in fixtureFiles do
        let p = fixturePath name
        File.WriteAllText(p, Artifact.render idl)
        printfn "regenerated %s/%s.json" fixtureDir name

// ---------------------------------------------------------------------------
// Compiling the claim.
// ---------------------------------------------------------------------------

/// Run a script through `dotnet fsi` and return its exit code plus both streams
/// joined. `None` when `dotnet` is not on PATH, which the caller skips on — the
/// alternative is a red gate on a machine that cannot run the probe at all.
let private runFsi (source: string) : (int * string) option =
    let path =
        Path.Combine(Path.GetTempPath(), sprintf "fuaran-consequence-%s.fsx" (Guid.NewGuid().ToString("N")))

    File.WriteAllText(path, source)

    try
        let psi = ChildProcess.redirected "dotnet" ("fsi \"" + path + "\"")

        match
            (try
                Some(Process.Start psi)
             with _ ->
                 None)
        with
        | None -> None
        | Some p ->
            let out = p.StandardOutput.ReadToEnd()
            let err = p.StandardError.ReadToEnd()
            p.WaitForExit()
            Some(p.ExitCode, out + "\n" + err)
    finally
        try
            File.Delete path
        with _ ->
            ()

/// The generated type declarations for one revision — the layer a consumer
/// compiles against.
let private types (idl: Idl) : string = Gen.fsharpTypes idl

// ---------------------------------------------------------------------------
// The command, exercised as a command.
//
// The classification is a library and is pinned above as one. What these legs pin
// is the COMMAND: its argument handling, its two calling shapes, and its exit
// codes — which are the part an external gate actually depends on and the part
// no library test touches. They shell the built dll rather than calling `main`
// in-process, because an exit code returned by a function is not evidence about a
// process.
//
// They live in this suite because the suite is what the repository gate runs, so
// the command is proven by the gate without the gate naming it.
// ---------------------------------------------------------------------------

/// The built CLI assembly. Located rather than assumed: the gate builds the
/// solution before running this suite, so it is present — and if it is not, that
/// is a FAILURE naming the build, never a skip. A skip here would let the whole
/// command leg read as green having exercised nothing.
let private cliDll () : string =
    let root = Path.GetDirectoryName(Snapshots.repoFile "Fuaran.Core.slnx")
    let bin = Path.Combine(root, "src", "Fuaran.Core.Idl.Cli", "bin")

    let found =
        if Directory.Exists bin then
            Directory.GetFiles(bin, "Fuaran.Core.Idl.Cli.dll", SearchOption.AllDirectories)
        else
            [||]

    if found.Length = 0 then
        failtestf "the CLI has not been built — run `dotnet build Fuaran.Core.slnx` (looked under %s)" bin
    else
        found |> Array.sortBy id |> Array.head

/// Invoke the command; return its exit code and both streams joined.
let private runCli (args: string) : int * string =
    let psi = ChildProcess.redirected "dotnet" ("\"" + cliDll () + "\" " + args)
    use p = Process.Start psi
    let out = p.StandardOutput.ReadToEnd()
    let err = p.StandardError.ReadToEnd()
    p.WaitForExit()
    p.ExitCode, out + "\n" + err

let private quoted (name: string) = "\"" + fixturePath name + "\""

[<Tests>]
let tests =
    testList
        "idl stability classes"
        [

          // -----------------------------------------------------------------
          // One classifier, two doors.
          // -----------------------------------------------------------------

          testCase "the Idl door and the artifact-text door give the same verdict" (fun _ ->
              let byValue = verdict Fixtures.baseIdl Fixtures.requiredFieldAdded

              let byText =
                  match
                      Diff.classifyArtifacts
                          (Artifact.render Fixtures.baseIdl)
                          (Artifact.render Fixtures.requiredFieldAdded)
                  with
                  | Ok v -> v
                  | Error e -> failtestf "classifyArtifacts: %s" e

              Expect.equal byText.Changes byValue.Changes "same rows"
              Expect.equal byText.Evolution byValue.Evolution "same wire evolution"

              Expect.equal
                  byText.FSharpConsequences
                  byValue.FSharpConsequences
                  "same F# consequences — the two entry points are one classifier")

          testCase "an unreadable artifact is a typed Error, not an exception" (fun _ ->
              match Diff.classifyArtifacts "{ not json" (Artifact.render Fixtures.baseIdl) with
              | Ok _ -> failtest "a malformed artifact classified successfully"
              | Error e -> Expect.stringStarts e "old: " "the error names which side failed")

          // -----------------------------------------------------------------
          // The F# consequence table — the rows where the two axes disagree.
          // -----------------------------------------------------------------

          testCase "a REQUIRED field added: breaking for emitters AND a construction break" (fun _ ->
              let v = verdict Fixtures.baseIdl Fixtures.requiredFieldAdded
              Expect.isTrue v.BreaksEmitters "the wire axis reports the emitter break"

              Expect.contains
                  v.FSharpConsequences
                  Diff.FullLiteralConstruction
                  "and the F# axis reports the construction break"

              Expect.contains
                  v.FSharpConsequences
                  Diff.StalePackageSlot
                  "a shape change always carries the stale-slot consequence")

          testCase "an OPTIONAL field added: additive on the wire, and STILL a construction break" (fun _ ->
              // The row this whole second axis exists for. Reading the wire verdict
              // alone says a consumer repins with no source change, and it does not:
              // a record literal must name every field, `option` or not.
              let v = verdict Fixtures.baseIdl Fixtures.optionalFieldAdded
              Expect.isFalse v.BreaksEmitters "additive for emitters"
              Expect.equal (Diff.verdictClass v |> Diff.classLabel) "additive" "and additive as a class"

              Expect.contains
                  v.FSharpConsequences
                  Diff.FullLiteralConstruction
                  "yet every full literal stops compiling")

          testCase "a union case added is a match consequence, not a construction one" (fun _ ->
              let cs = consequencesOf Fixtures.baseIdl Fixtures.unionCaseAdded
              Expect.contains cs Diff.ExhaustiveMatch "the DU gained a case"

              Expect.isFalse
                  (List.contains Diff.FullLiteralConstruction cs)
                  "no record literal moved — the two sites are not interchangeable")

          testCase "an enum case added is a match consequence too" (fun _ ->
              Expect.contains
                  (consequencesOf Fixtures.baseIdl Fixtures.enumCaseAdded)
                  Diff.ExhaustiveMatch
                  "an enum emits as a closed DU, so the same site breaks")

          testCase "a kind removed breaks matches over the node-kind discriminator" (fun _ ->
              Expect.contains
                  (consequencesOf Fixtures.baseIdl Fixtures.kindRemoved)
                  Diff.ExhaustiveMatch
                  "a kind is a case of the generated node-kind DU")

          testCase "an optionality move that keeps the F# type reports NO shape change" (fun _ ->
              // required -> omitDefault is wire-visible (the omit-at-default bytes
              // move) and emits the same non-`option` member, so the two axes part
              // company in the other direction. Decidable from the artifact, so
              // decided — not reported conservatively.
              let v = verdict Fixtures.baseIdl Fixtures.omitDefaultInstead

              Expect.isNonEmpty v.Changes "the wire axis reports the change"

              Expect.equal
                  v.FSharpConsequences
                  [ Diff.NoGeneratedShapeChange ]
                  "and the F# axis reports that no declaration moved")

          testCase "an identical pair is unchanged, with no consequences at all" (fun _ ->
              let v = verdict Fixtures.baseIdl Fixtures.baseIdl
              Expect.isEmpty v.Changes "no rows"
              Expect.isEmpty v.FSharpConsequences "no consequences"
              Expect.equal (Diff.verdictClass v |> Diff.classLabel) "unchanged" "class")

          testCase "every consequence class has a distinct label and a reason" (fun _ ->
              let labels = Diff.allConsequences |> List.map Diff.consequenceLabel

              Expect.equal
                  (labels |> List.distinct |> List.length)
                  labels.Length
                  "the labels are a contract — they must be distinct"

              for c in Diff.allConsequences do
                  Expect.isGreaterThan
                      (Diff.consequenceWhy c).Length
                      40
                      (sprintf "%s states why it applies" (Diff.consequenceLabel c)))

          // -----------------------------------------------------------------
          // The wire-profile bump.
          // -----------------------------------------------------------------

          testCase "an additive revision bumps the MINOR, through Versioning.bump" (fun _ ->
              let v = verdict Fixtures.baseIdl Fixtures.optionalFieldAdded

              match Diff.bumpProfile Versioning.Profile.coreV1 v with
              | Diff.Bump.Bumped p ->
                  Expect.equal (Versioning.Profile.render p) "core@1.1" "the minor moved, the major did not"
              | Diff.Bump.Undecided _ -> failtest "an additive revision was reported undecided")

          testCase "a breaking-wire revision bumps the MAJOR and resets the minor" (fun _ ->
              let v = verdict Fixtures.baseIdl Fixtures.kindRemoved

              Expect.isTrue
                  (v.Changes |> List.exists (fun c -> c.Severity = Diff.BreakingWire))
                  "a removed kind is a wire break"

              match Diff.bumpProfile { Name = "core"; Major = 1; Minor = 7 } v with
              | Diff.Bump.Bumped p -> Expect.equal (Versioning.Profile.render p) "core@2.0" "a /vN/ boundary"
              | Diff.Bump.Undecided _ -> failtest "a wire break was reported undecided")

          testCase "an emitter break rides the MINOR, and says so separately" (fun _ ->
              // The minor is the honest answer to the question the profile asks —
              // every existing document still decodes, so a consumer is `Behind`,
              // not `Foreign`. What it cannot carry is the emitter obligation, which
              // is why that travels as its own field rather than as a major bump.
              let v = verdict Fixtures.baseIdl Fixtures.requiredFieldAdded

              match Diff.bumpProfile Versioning.Profile.coreV1 v with
              | Diff.Bump.Bumped p -> Expect.equal (Versioning.Profile.render p) "core@1.1" "minor"
              | Diff.Bump.Undecided _ -> failtest "reported undecided"

              Expect.isTrue v.BreaksEmitters "and the emitter obligation is carried, not lost"
              Expect.equal (Diff.verdictClass v |> Diff.classLabel) "breaking" "the gate class is breaking")

          testCase "an additive revision with no rows leaves the profile where it is" (fun _ ->
              match Diff.bumpProfile Versioning.Profile.coreV1 (verdict Fixtures.baseIdl Fixtures.baseIdl) with
              | Diff.Bump.Bumped p -> Expect.equal (Versioning.Profile.render p) "core@1.0" "no movement"
              | Diff.Bump.Undecided _ -> failtest "an unchanged pair was reported undecided")

          testCase "the class vocabulary round-trips, and an unknown label is refused" (fun _ ->
              for c in Diff.allClasses do
                  Expect.equal (Diff.classOfLabel (Diff.classLabel c)) (Some c) "label round-trips"

              Expect.isNone (Diff.classOfLabel "Additive") "the vocabulary is case-sensitive"
              Expect.isNone (Diff.classOfLabel "safe") "an unknown label is None, never a default")

          testCase "the exit codes separate absorbable, breaking and undecided" (fun _ ->
              Expect.equal (Diff.exitCode Diff.VerdictClass.Unchanged) 0 "unchanged"
              Expect.equal (Diff.exitCode Diff.VerdictClass.HostSurface) 0 "host-surface"
              Expect.equal (Diff.exitCode Diff.VerdictClass.Additive) 0 "additive"
              Expect.equal (Diff.exitCode Diff.VerdictClass.Breaking) 3 "breaking"
              Expect.equal (Diff.exitCode Diff.VerdictClass.Undecided) 4 "undecided")

          testCase "the verdict block names the class, the evolution and the consequences" (fun _ ->
              let v = verdict Fixtures.baseIdl Fixtures.requiredFieldAdded
              let block = Diff.verdictBlock v
              Expect.stringContains block "class:" "the class line"
              Expect.stringContains block "breaking" "the class itself"
              Expect.stringContains block "full-literal-construction" "the consequence label"
              Expect.stringContains block "FS0764" "and the compiler code a reader will meet")

          // -----------------------------------------------------------------
          // The property: the reported class is the class the generator's output
          // actually exhibits.
          //
          // FALSIFIER — if the classifier reported `FullLiteralConstruction` for a
          // change that left every generated record member untouched, or reported
          // `ExhaustiveMatch` for one that left every generated DU case untouched,
          // this fails. It is deliberately a property of the EMITTED SOURCE rather
          // than of the classifier's own reasoning: restating the mapping in the
          // assertion would certify this file against itself.
          // -----------------------------------------------------------------

          testCase "every perturbation's reported consequence matches the generator's output" (fun _ ->
              let cases =
                  [ "a required field added", Fixtures.requiredFieldAdded
                    "an optional field added", Fixtures.optionalFieldAdded
                    "a union case added", Fixtures.unionCaseAdded
                    "an enum case added", Fixtures.enumCaseAdded
                    "an optionality move that keeps the type", Fixtures.omitDefaultInstead ]

              let before = types Fixtures.baseIdl

              /// The generated record MEMBERS and DU CASES, as the emitted source
              /// spells them — read off the text, never re-derived from the IDL.
              let members (src: string) =
                  src.Replace("\r\n", "\n").Split('\n')
                  |> Array.map (fun l -> l.Trim())
                  |> Array.filter (fun l -> l.Contains ": " && not (l.StartsWith "///"))
                  |> Set.ofArray

              let duCases (src: string) =
                  src.Replace("\r\n", "\n").Split('\n')
                  |> Array.map (fun l -> l.Trim())
                  |> Array.filter (fun l -> l.StartsWith "| ")
                  |> Set.ofArray

              for (name, after) in cases do
                  let afterSrc = types after
                  let cs = consequencesOf Fixtures.baseIdl after
                  let membersMoved = members before <> members afterSrc
                  let casesMoved = duCases before <> duCases afterSrc

                  Expect.equal
                      (List.contains Diff.FullLiteralConstruction cs)
                      membersMoved
                      (sprintf "%s: the construction consequence tracks the emitted record members" name)

                  Expect.equal
                      (List.contains Diff.ExhaustiveMatch cs)
                      casesMoved
                      (sprintf "%s: the match consequence tracks the emitted DU cases" name))

          testCase "the property leg is not vacuous — at least one case moves each site" (fun _ ->
              // The guard the leg above needs: if `Gen.fsharpTypes` ever stopped
              // emitting members or cases, every comparison there would be
              // false-equals-false and the leg would pass having measured nothing.
              let src = types Fixtures.baseIdl
              Expect.stringContains src "Body" "the emitted record carries its members"
              Expect.stringContains src "| Inline" "and the emitted DU carries its cases")

          // -----------------------------------------------------------------
          // The two compiled exemplars. The real compiler, over the real emitted
          // layer, in both directions.
          // -----------------------------------------------------------------

          testCase "FS0764 compiled: a full literal over the previous fields stops compiling" (fun _ ->
              // The generated spec record for `Note` gains `Caption`. A consumer's
              // full literal naming only the previous two fields must fail, and the
              // same literal with the new field must succeed — the second half is
              // what proves the first is measuring the field and not the harness.
              let afterSrc = types Fixtures.optionalFieldAdded

              let literal (withCaption: bool) =
                  afterSrc
                  + "\n\nlet probe: NoteSpec =\n    { Body = \"b\"\n      Loudness = None"
                  + (if withCaption then "\n      Caption = None" else "")
                  + " }\n\nprintfn \"%s\" probe.Body\n"

              match runFsi (literal false), runFsi (literal true) with
              | None, _
              | _, None -> skiptest "dotnet not on PATH — the FS0764 compile leg is skipped"
              | Some(stale, staleOut), Some(adapted, adaptedOut) ->
                  Expect.notEqual stale 0 "the stale literal must not compile"

                  Expect.stringContains
                      staleOut
                      "FS0764"
                      "and it must fail for the stated reason: no assignment given for the new field"

                  Expect.equal adapted 0 (sprintf "the adapted literal must compile: %s" adaptedOut))

          testCase "FS0025 compiled: an exhaustive match over the previous cases stops being exhaustive" (fun _ ->
              // `Source` gains `Computed`. A consumer's match over the previous two
              // cases must warn FS0025, and the match that adds the new arm must not
              // — again both directions, so a leg that has stopped seeing the
              // warning at all cannot read as a pass.
              let afterSrc = types Fixtures.unionCaseAdded

              let matcher (withComputed: bool) =
                  afterSrc
                  + "\n\nlet describe (s: Source) =\n    match s with\n"
                  + "    | Source.Inline text -> text\n    | Source.Ref id -> id\n"
                  + (if withComputed then
                         "    | Source.Computed -> \"computed\"\n"
                     else
                         "")
                  + "\nprintfn \"%s\" (describe (Source.Ref \"x\"))\n"

              match runFsi (matcher false), runFsi (matcher true) with
              | None, _
              | _, None -> skiptest "dotnet not on PATH — the FS0025 compile leg is skipped"
              | Some(_, staleOut), Some(_, adaptedOut) ->
                  Expect.stringContains staleOut "FS0025" "the match that predates the new case is reported incomplete"

                  Expect.isFalse
                      (adaptedOut.Contains "FS0025")
                      "and the match that covers it is not — the leg is measuring the case, not the harness")

          // -----------------------------------------------------------------
          // The committed artefacts: the CLI fixtures, and the `docs/` table.
          // -----------------------------------------------------------------

          testCase "the committed CLI fixtures are what the declarations render" (fun _ ->
              for (name, idl) in fixtureFiles do
                  let p = fixturePath name

                  Expect.isTrue
                      (File.Exists p)
                      (sprintf
                          "%s/%s.json is missing — regenerate with `dotnet run --project tests/Fuaran.Core.Tests -- --regen-idl-classify-fixtures`"
                          fixtureDir
                          name)

                  Expect.equal
                      ((File.ReadAllText p).Replace("\r\n", "\n"))
                      ((Artifact.render idl).Replace("\r\n", "\n"))
                      (sprintf
                          "%s/%s.json is stale — regenerate with `dotnet run --project tests/Fuaran.Core.Tests -- --regen-idl-classify-fixtures`"
                          fixtureDir
                          name))

          testCase "the committed fixtures classify as the command legs assert" (fun _ ->
              // The command legs below invoke `--expect breaking` on one pair and
              // read exit 0 on the other. If those two classes ever moved, those legs
              // would fail through a child process's exit code; this says the same
              // thing in-process, where the reason is legible.
              Expect.equal (classOf Fixtures.baseIdl Fixtures.requiredFieldAdded) "breaking" "the required-field pair"
              Expect.equal (classOf Fixtures.baseIdl Fixtures.optionalFieldAdded) "additive" "the optional-field pair")

          testCase "the docs table names every consequence class" (fun _ ->
              // The table is rendered from the code; `docs/` carries it for a reader
              // with no build. This is the drift guard between them — a class added
              // without a paragraph fails here rather than leaving an external gate
              // citing a table that does not mention what it reported.
              let doc = Snapshots.repoFile "docs/idl-stability-classes.md"

              Expect.isTrue (File.Exists doc) "docs/idl-stability-classes.md exists"
              let text = File.ReadAllText doc

              for c in Diff.allConsequences do
                  Expect.stringContains text (Diff.consequenceLabel c) "the document names the class"

              for c in Diff.allClasses do
                  Expect.stringContains text (Diff.classLabel c) "and the verdict class")

          // -----------------------------------------------------------------
          // The command, over the committed fixtures. Both calling shapes, both
          // directions of the assertion, and the refusals.
          // -----------------------------------------------------------------

          testCase "the command asserts a declared class, and fails when the artifacts disagree" (fun _ ->
              // The form that belongs in a specification home's gate: the author
              // declares the class, and the command goes red when the artifacts say
              // otherwise. Both directions on the SAME pair, so a leg that has
              // stopped reading the artifacts at all cannot read as a pass.
              let matched, matchedOut =
                  runCli (sprintf "classify %s %s --expect breaking" (quoted "before") (quoted "after-required-field"))

              Expect.equal matched 0 (sprintf "the declared class holds: %s" matchedOut)
              Expect.stringContains matchedOut "MATCHED" "and says so"

              let mismatched, mismatchedOut =
                  runCli (sprintf "classify %s %s --expect additive" (quoted "before") (quoted "after-required-field"))

              Expect.equal mismatched 1 "a wrong declaration exits 1 — not 0, and not a verdict code"
              Expect.stringContains mismatchedOut "breaking" "and names what the artifacts actually classify as")

          testCase "the command's branching exit codes distinguish additive from breaking" (fun _ ->
              let additive, _ =
                  runCli (sprintf "classify %s %s" (quoted "before") (quoted "after-optional-field"))

              Expect.equal additive 0 "an optional field added is absorbed by repinning"

              let breaking, breakingOut =
                  runCli (sprintf "classify %s %s" (quoted "before") (quoted "after-required-field"))

              Expect.equal breaking 3 "a required field added is a break a gate must stop on"

              Expect.stringContains
                  breakingOut
                  "full-literal-construction"
                  "and the F# consequence travels with it, which is the whole point of the second axis"

              let unchanged, _ =
                  runCli (sprintf "classify %s %s" (quoted "before") (quoted "before"))

              Expect.equal unchanged 0 "an identical pair")

          testCase "the command refuses rather than reaching a verdict" (fun _ ->
              // Exit 2 is deliberately not one of the verdict codes: the tool reached
              // no verdict, and a gate must not read that as one.
              let missing, missingOut =
                  runCli (sprintf "classify %s \"no-such-file.json\"" (quoted "before"))

              Expect.equal missing 2 "an unreadable input refuses"
              Expect.stringContains missingOut "not found" "naming what it could not read"

              let badClass, badClassOut =
                  runCli (sprintf "classify %s %s --expect safe" (quoted "before") (quoted "after-required-field"))

              Expect.equal badClass 2 "an unknown --expect class refuses"

              Expect.stringContains badClassOut "unknown --expect class" "rather than silently dropping the assertion"

              let badFlag, _ =
                  runCli (sprintf "classify %s %s --strict" (quoted "before") (quoted "after-required-field"))

              Expect.equal badFlag 2 "an unrecognised option refuses — a misspelled flag must not pass"

              let noArgs, noArgsOut = runCli "classify"
              Expect.equal noArgs 2 "classify needs its two paths"
              Expect.stringContains noArgsOut "two paths" "and says which")

          testCase "the command prints the consequence table the docs restate" (fun _ ->
              let code, out = runCli "table"
              Expect.equal code 0 "table exits 0"

              for c in Diff.allConsequences do
                  Expect.stringContains out (Diff.consequenceLabel c) "the table names every class") ]
