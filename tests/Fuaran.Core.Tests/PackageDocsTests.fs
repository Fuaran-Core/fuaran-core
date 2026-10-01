module Fuaran.Core.Tests.PackageDocsTests

// ---------------------------------------------------------------------------
// Phase 254 — every package carries its XML documentation file.
//
// A consumer restoring a `Fuaran.Core.*` package from the registry used to get IntelliSense with
// signatures and no text: the projects did not generate the documentation file, so the doc comments
// reached a consumer only inside the `fable/` source copy the package ships for Fable, which an
// editor does not read for a referenced assembly. `Directory.Build.props` now turns
// `GenerateDocumentationFile` on for every project under `src/`, and the SDK packs the file beside
// the assembly.
//
// What this file holds, and why each property is there:
//
//   1. Every packable project's assembly has its `.xml` beside it in this suite's own output — the
//      build ACTUALLY produced it, which is the claim, rather than a property that says it should.
//      The test project references every packable project, so the SDK copies each reference's
//      documentation file next to it; a package added without one is red here, by name.
//   2. That file documents the assembly it sits beside: `<assembly><name>` is the assembly's, and it
//      carries at least one member — an empty or misnamed file is no documentation.
//   3. No packable project switches the property off in its own file. Property 1 reads a build
//      output, and a stale `.xml` left in an output directory by an earlier build would satisfy it
//      after the property was removed; the declaration side is checked so that removal is red on
//      the first build rather than on the first clean one.
//
// Each reader has a go-red case beside it over synthetic input, so a reader that matched nothing
// cannot pass the live case by accident.
//
// The roster is `PackageRosterTests.packableProjects` — the ONE derivation of "what ships".
// ---------------------------------------------------------------------------

open System
open System.IO
open System.Text.RegularExpressions
open System.Xml.Linq
open Expecto

/// What is wrong with `xmlText` as the documentation file of `assemblyName`, or `None` when it is
/// that file: a parseable `<doc>`, naming the assembly, with at least one documented member.
let internal docFileFault (assemblyName: string) (xmlText: string) : string option =
    try
        let doc = XDocument.Parse xmlText
        let root = doc.Root

        if isNull root || root.Name.LocalName <> "doc" then
            Some "the root element is not <doc>"
        else
            let named =
                match root.Element(XName.Get "assembly") with
                | null -> None
                | a ->
                    match a.Element(XName.Get "name") with
                    | null -> None
                    | n -> Some(n.Value.Trim())

            let memberCount =
                match root.Element(XName.Get "members") with
                | null -> 0
                | ms -> ms.Elements(XName.Get "member") |> Seq.length

            match named with
            | None -> Some "it names no assembly"
            | Some n when n <> assemblyName -> Some(sprintf "it documents '%s', not '%s'" n assemblyName)
            | Some _ when memberCount = 0 -> Some "it documents no member"
            | Some _ -> None
    with e ->
        Some("it does not parse: " + e.Message)

let private generateDocsOffRe =
    Regex(@"<GenerateDocumentationFile>\s*false\s*</GenerateDocumentationFile>", RegexOptions.IgnoreCase)

/// A project file that switches the documentation file OFF in its own body — which, read after
/// `Directory.Build.props`, would win over the repository-wide setting.
let internal switchesDocsOff (projectText: string) : bool = generateDocsOffRe.IsMatch projectText

/// The property `Directory.Build.props` sets, scoped to the projects under `src/`.
let internal propsGeneratesDocsForSrc (propsText: string) : bool =
    Regex.IsMatch(
        propsText,
        @"<PropertyGroup\s+Condition=""[^""]*'src'[^""]*"">\s*<GenerateDocumentationFile>\s*true\s*</GenerateDocumentationFile>",
        RegexOptions.IgnoreCase
    )

let private repoRoot () : string =
    Path.GetDirectoryName(Snapshots.repoFile "Fuaran.Core.slnx")

[<Tests>]
let tests =
    testList
        "PackageDocs"
        [ test "every packable project's built assembly has its XML documentation file beside it" {
              let root = repoRoot ()
              let packable = PackageRosterTests.packableProjects root
              Expect.isNonEmpty packable "the packable roster is empty — the derivation found nothing to check"

              let faults =
                  packable
                  |> List.choose (fun p ->
                      // No packable project sets <AssemblyName>, so the assembly is the project's base name.
                      let assembly = Path.GetFileNameWithoutExtension p.ProjectFile
                      let dll = Path.Combine(AppContext.BaseDirectory, assembly + ".dll")
                      let xml = Path.Combine(AppContext.BaseDirectory, assembly + ".xml")

                      if not (File.Exists dll) then
                          Some(
                              sprintf
                                  "%s: no %s.dll in the suite's output — is it referenced by the test project?"
                                  p.PackageId
                                  assembly
                          )
                      elif not (File.Exists xml) then
                          Some(sprintf "%s: %s.dll ships without %s.xml" p.PackageId assembly assembly)
                      else
                          docFileFault assembly (File.ReadAllText xml)
                          |> Option.map (fun why -> sprintf "%s: %s.xml — %s" p.PackageId assembly why))

              if not (List.isEmpty faults) then
                  failtestf
                      "a package without its documentation file reaches a consumer as signatures and no text:\n  %s"
                      (String.concat "\n  " faults)
          }

          test
              "Directory.Build.props generates the documentation file for src/, and no packable project switches it off" {
              let root = repoRoot ()
              let props = File.ReadAllText(Path.Combine(root, "Directory.Build.props"))

              Expect.isTrue
                  (propsGeneratesDocsForSrc props)
                  "Directory.Build.props no longer sets GenerateDocumentationFile for the projects under src/"

              let off =
                  PackageRosterTests.packableProjects root
                  |> List.filter (fun p -> switchesDocsOff (File.ReadAllText(Path.Combine(root, p.ProjectFile))))
                  |> List.map _.ProjectFile

              Expect.isEmpty off "these packable projects switch the documentation file off in their own body"
          }

          test "the documentation-file reader refuses a missing name, a foreign name, an empty file and a non-doc" {
              let ok =
                  "<?xml version=\"1.0\"?><doc><assembly><name>A</name></assembly><members><member name=\"T:A.X\"><summary>x</summary></member></members></doc>"

              Expect.isNone (docFileFault "A" ok) "a well-formed file for A is accepted"
              Expect.isSome (docFileFault "B" ok) "a file documenting A is not B's"

              Expect.isSome
                  (docFileFault "A" "<doc><assembly><name>A</name></assembly><members></members></doc>")
                  "a file documenting no member is refused"

              Expect.isSome (docFileFault "A" "<doc><members/></doc>") "a file naming no assembly is refused"
              Expect.isSome (docFileFault "A" "<notdoc/>") "a non-<doc> root is refused"
              Expect.isSome (docFileFault "A" "not xml") "unparseable text is refused"
          }

          test "the declaration readers see the src/ scope and an opt-out" {
              let props =
                  "<Project><PropertyGroup Condition=\"'$(X)' == 'src'\">\n    <GenerateDocumentationFile>true</GenerateDocumentationFile>\n  </PropertyGroup></Project>"

              Expect.isTrue (propsGeneratesDocsForSrc props) "the scoped group is recognised"

              Expect.isFalse
                  (propsGeneratesDocsForSrc "<Project><PropertyGroup><Version>1</Version></PropertyGroup></Project>")
                  "a props file without it is not"

              Expect.isTrue
                  (switchesDocsOff
                      "<PropertyGroup><GenerateDocumentationFile>false</GenerateDocumentationFile></PropertyGroup>")
                  "an explicit opt-out is seen"

              Expect.isFalse
                  (switchesDocsOff "<PropertyGroup><IsPackable>true</IsPackable></PropertyGroup>")
                  "silence is not one"
          } ]
