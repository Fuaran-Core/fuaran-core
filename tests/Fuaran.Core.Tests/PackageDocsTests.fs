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
// The roster is `Roster.derive` — the ONE derivation of "what ships".
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
              let packable = Roster.derive root
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
                  Roster.derive root
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

// ---------------------------------------------------------------------------
// Phase 339 — every public member carries a doc comment; Phase 537 — held as an invariant.
//
// The documentation-file property above says a package ships ITS doc comments; it says nothing
// about whether a given member has one. A consumer hovering an undocumented union case sees its
// name and nothing else. This block counts, per package, the public members whose XML
// documentation file carries no text for them, against the committed public surface in
// `api/*.txt` — the ONE spelling of "what is public" (PublicSurfaceTests holds it to the built
// assembly).
//
// The invariant is ZERO, held directly: a public member that is undocumented is red, BY NAME, at
// the commit that adds it or deletes its comment. Phase 339 reached zero through a per-package
// ratchet file of allowed exceptions; once every list was empty the file stated this one sentence
// as nineteen rows of data with a switch and a reader of its own, so Phase 537 deleted it (the
// record is DECISIONS.md). A member that genuinely cannot carry a comment is a token class the
// compiler writes: `docId` skips it, and it is listed below with its reason, where a reviewer sees it.
//
// What is not counted, and why (each is a token the compiler writes, not one a source line can
// document) — `docId` below skips each:
//
//   - `ctor` — a record's constructor is generated from its fields, and a class's primary
//     constructor is documented by the type that declares it;
//   - a union's `Tags` class and its `Tags.<Case>` literals — generated tag constants;
//   - a union case's nested class (`type U+Case (type)`) — the case is counted once, as its
//     `union-case` token;
//   - `interface-marker` — it names no member.
// ---------------------------------------------------------------------------

let private genericArityBeforeArgs = Regex(@"`\d+<")

/// A parameter type as a baseline spells it (`FSharpList`1<!!0>`, `Outer+Inner`), in the spelling
/// of an XML documentation id (`FSharpList{``0}`, `Outer.Inner`).
let internal docIdType (t: string) : string =
    let t = Regex.Replace(t.Trim(), @"!!(\d+)", "``$1")
    let t = Regex.Replace(t, @"!(\d+)", "`$1")

    genericArityBeforeArgs
        .Replace(t, "<")
        .Replace("<", "{")
        .Replace(">", "}")
        .Replace(", ", ",")
        .Replace("+", ".")
        .Replace("&", "@")

/// The parameter list of a baseline `method` token, split at the commas outside any `<…>`.
let private splitTopLevel (ps: string) : string list =
    let parts = ResizeArray<string>()
    let current = Text.StringBuilder()
    let mutable depth = 0

    for ch in ps do
        match ch with
        | '<' ->
            depth <- depth + 1
            current.Append ch |> ignore
        | '>' ->
            depth <- depth - 1
            current.Append ch |> ignore
        | ',' when depth = 0 ->
            parts.Add(current.ToString())
            current.Clear() |> ignore
        | c -> current.Append c |> ignore

    parts.Add(current.ToString())
    List.ofSeq parts

let private typeTokenRe = Regex(@"^type (\S+) \((\w+)\)$")
let private unionCaseTokenRe = Regex(@"^union-case (\S+)\.(\w+) #\d+\((.*)\)$")
let private memberNameRe = Regex(@"^(?:record-field|property|field) (\S+) ")
let private methodTokenRe = Regex(@"^method (.+?)\((.*)\) : .*$")

/// The XML documentation id of the member a baseline token names — `None` for a token of an
/// excluded class (listed in the block comment above). `unions` is the package's union types, as
/// the baseline spells them, so a union case's nested class can be told from an ordinary nested type.
let internal docId (unions: Set<string>) (token: string) : string option =
    let dotted (s: string) = s.Replace("+", ".")

    if token.StartsWith("type ", StringComparison.Ordinal) then
        let m = typeTokenRe.Match token

        if not m.Success then
            None
        else
            let full = m.Groups[1].Value
            let kind = m.Groups[2].Value
            let cut = full.LastIndexOf '+'
            let parent = if cut < 0 then None else Some(full.Substring(0, cut))

            if full.EndsWith("+Tags", StringComparison.Ordinal) then
                None
            elif kind = "type" && (parent |> Option.exists unions.Contains) then
                None
            else
                Some("T:" + dotted full)
    elif token.StartsWith("union-case ", StringComparison.Ordinal) then
        let m = unionCaseTokenRe.Match token

        if not m.Success then
            None
        else
            // A carrying case's token names its factory (`NewCase`); a nullary case's names the case.
            let name = m.Groups[2].Value

            let case =
                if m.Groups[3].Value <> "" && name.StartsWith("New", StringComparison.Ordinal) then
                    name.Substring 3
                else
                    name

            Some("T:" + dotted m.Groups[1].Value + "." + case)
    elif
        token.StartsWith("record-field ", StringComparison.Ordinal)
        || token.StartsWith("property ", StringComparison.Ordinal)
    then
        let m = memberNameRe.Match token

        if m.Success then
            Some("P:" + dotted m.Groups[1].Value)
        else
            None
    elif token.StartsWith("field ", StringComparison.Ordinal) then
        // An F# literal documents as a property; a tag literal is generated.
        let m = memberNameRe.Match token

        if not m.Success || m.Groups[1].Value.Contains "+Tags." then
            None
        else
            Some("P:" + dotted m.Groups[1].Value)
    elif token.StartsWith("method ", StringComparison.Ordinal) then
        let m = methodTokenRe.Match token

        if not m.Success then
            None
        else
            let name = dotted (Regex.Replace(m.Groups[1].Value, @"`(\d+)$", "``$1"))
            let ps = m.Groups[2].Value

            if ps.Trim() = "" then
                Some("M:" + name)
            else
                Some(
                    "M:"
                    + name
                    + "("
                    + (splitTopLevel ps |> List.map docIdType |> String.concat ",")
                    + ")"
                )
    else
        None

/// The documentation ids an XML documentation file gives TEXT to — a `<member>` whose comment is
/// blank documents nothing. An INDEXED property's id carries its parameter list
/// (`P:Fuaran.Core.Vector`1.Item(System.Int32)`) where the baseline's `property` token, and so
/// `docIdOf`, names the property alone; the list is dropped so the two spell one member (Phase 417).
let internal documentedIds (xmlText: string) : Set<string> =
    let doc = XDocument.Parse xmlText

    let propertyAlone (id: string) =
        if id.StartsWith("P:", StringComparison.Ordinal) then
            match id.IndexOf '(' with
            | -1 -> id
            | paren -> id.Substring(0, paren)
        else
            id

    match doc.Root with
    | null -> Set.empty
    | root ->
        match root.Element(XName.Get "members") with
        | null -> Set.empty
        | ms ->
            ms.Elements(XName.Get "member")
            |> Seq.choose (fun m ->
                match m.Attribute(XName.Get "name") with
                | null -> None
                | _ when String.IsNullOrWhiteSpace m.Value -> None
                | a -> Some(propertyAlone a.Value))
            |> Set.ofSeq

/// One package's measurement: how many members it counts, and which of them carry no doc comment.
type internal DocCoverage =
    { Package: string
      Total: int
      Undocumented: string list }

/// Measure one package: every counted token of its baseline against the ids its documentation
/// file documents.
let internal measure (package: string) (baseline: string list) (documented: Set<string>) : DocCoverage =
    let unions =
        baseline
        |> List.choose (fun t ->
            let m = typeTokenRe.Match t

            if m.Success && m.Groups[2].Value = "union" then
                Some m.Groups[1].Value
            else
                None)
        |> Set.ofList

    let ids = baseline |> List.choose (docId unions) |> List.distinct

    { Package = package
      Total = ids.Length
      Undocumented = ids |> List.filter (fun i -> not (documented.Contains i)) |> List.sort }

/// The invariant's findings: one per package that carries an undocumented public member, naming
/// each member. Empty is green.
let internal undocumentedFindings (measured: DocCoverage list) : string list =
    [ for m in measured do
          if not m.Undocumented.IsEmpty then
              yield
                  sprintf
                      "%s: %d public member(s) carry no doc comment — write one (what it is for, what it refuses, the unit or invariant it holds):\n      %s"
                      m.Package
                      m.Undocumented.Length
                      (String.concat "\n      " m.Undocumented) ]

/// Every packable package measured from its committed baseline and its built documentation file.
let private measureAll () : Result<DocCoverage list, string list> =
    let root = repoRoot ()

    let results =
        Roster.derive root
        |> List.map (fun p ->
            let assembly = Path.GetFileNameWithoutExtension p.ProjectFile
            let xml = Path.Combine(AppContext.BaseDirectory, assembly + ".xml")
            let baseline = PublicSurfaceTests.baselinePath p.PackageId

            if not (File.Exists xml) then
                Error(sprintf "%s: no %s.xml in the suite's output" p.PackageId assembly)
            elif not (File.Exists baseline) then
                Error(sprintf "%s: no committed baseline %s" p.PackageId baseline)
            else
                let tokens = PublicSurfaceTests.baselineTokens (File.ReadAllText baseline)
                Ok(measure p.PackageId tokens (documentedIds (File.ReadAllText xml))))

    match
        results
        |> List.choose (function
            | Error e -> Some e
            | Ok _ -> None)
    with
    | [] ->
        Ok(
            results
            |> List.choose (function
                | Ok m -> Some m
                | Error _ -> None)
        )
    | errors -> Error errors

[<Tests>]
let coverageTests =
    testList
        "PackageDocs coverage"
        [ test "every public member carries a doc comment" {
              let measured =
                  match measureAll () with
                  | Ok ms -> ms
                  | Error es -> failtestf "the coverage could not be measured:\n  %s" (String.concat "\n  " es)

              Expect.isNonEmpty measured "no package was measured — the roster derivation found nothing"

              Expect.isGreaterThan
                  (measured |> List.sumBy _.Total)
                  0
                  "no member was counted — a reader that counted nothing would pass vacuously"

              for m in measured do
                  printfn
                      "doc coverage %-28s %4d/%-4d (%d undocumented)"
                      m.Package
                      (m.Total - m.Undocumented.Length)
                      m.Total
                      m.Undocumented.Length

              match undocumentedFindings measured with
              | [] -> ()
              | findings -> failtestf "undocumented public members:\n  %s" (String.concat "\n  " findings)
          }

          test
              "the documentation-id reader spells each counted token as the compiler does, and skips the generated ones" {
              let unions = Set.ofList [ "N.U"; "N.M+V`1" ]

              let cases =
                  [ "type N.M (module)", Some "T:N.M"
                    "type N.U (union)", Some "T:N.U"
                    "type N.U+Tags (type)", None
                    "type N.U+Leaf (type)", None
                    "type N.M+R (record)", Some "T:N.M.R"
                    "type N.M+V`1+Case (type)", None
                    "union-case N.U.Empty #0()", Some "T:N.U.Empty"
                    "union-case N.U.NewLeaf #1(Item: System.String)", Some "T:N.U.Leaf"
                    "record-field N.M+R.Name #0 : System.String", Some "P:N.M.R.Name"
                    "property N.M.empty : N.U { get }", Some "P:N.M.empty"
                    "field N.M.depth : System.Int32 (literal)", Some "P:N.M.depth"
                    "field N.U+Tags.Leaf : System.Int32 (literal)", None
                    "ctor N.M+R..ctor(System.String)", None
                    "interface-marker N.I", None
                    "method N.M.run() : System.Void", Some "M:N.M.run"
                    "method N.M.f(System.String, System.Int32) : System.String",
                    Some "M:N.M.f(System.String,System.Int32)"
                    "method N.M.g`2(Microsoft.FSharp.Core.FSharpFunc`2<!!0, !!1>, N.M+V`1<!!0>) : !!1",
                    Some "M:N.M.g``2(Microsoft.FSharp.Core.FSharpFunc{``0,``1},N.M.V{``0})"
                    "method N.M+V`1.Put(System.String, !0, System.Byte[]) : System.Void",
                    Some "M:N.M.V`1.Put(System.String,`0,System.Byte[])" ]

              for token, expected in cases do
                  Expect.equal (docId unions token) expected token
          }

          test "the invariant names each undocumented member by package, and is silent at zero" {
              let clean =
                  [ { Package = "A"
                      Total = 2
                      Undocumented = [] } ]

              Expect.isEmpty (undocumentedFindings clean) "zero undocumented members is green"

              let planted =
                  clean
                  @ [ { Package = "B"
                        Total = 3
                        Undocumented = [ "M:B.z"; "P:B.y" ] } ]

              match undocumentedFindings planted with
              | [ one ] ->
                  Expect.stringContains one "B: 2 public member(s)" "the package and the count are named"
                  Expect.stringContains one "M:B.z" "each member is named"
                  Expect.stringContains one "P:B.y" "each member is named"
              | other -> failtestf "expected one finding for B, got %A" other
          }

          test "a member counts as documented only when its comment carries text" {
              let xml =
                  "<doc><assembly><name>A</name></assembly><members>"
                  + "<member name=\"T:A.X\"><summary>The x.</summary></member>"
                  + "<member name=\"T:A.Y\"><summary> </summary></member>"
                  + "</members></doc>"

              let ids = documentedIds xml
              Expect.isTrue (ids.Contains "T:A.X") "a member with a summary is documented"
              Expect.isFalse (ids.Contains "T:A.Y") "a blank summary documents nothing"

              let m = measure "A" [ "type A.X (type)"; "type A.Y (type)"; "type A.Z (type)" ] ids
              Expect.equal m.Total 3 "every counted token is measured"
              Expect.equal m.Undocumented [ "T:A.Y"; "T:A.Z" ] "the blank and the absent are both undocumented"
          } ]
