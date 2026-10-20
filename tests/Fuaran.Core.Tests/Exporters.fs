module Fuaran.Core.Tests.Exporters

// ============================================================================
//  The exporter table (Phase 508): one row per family this repository emits as a committed file.
//
//  Seven `--emit-*` verbs grew one mechanism each — three JSON writers, a freshness equality per
//  family, and two manifest shapes. They are rows now. Every row renders through the one writer
//  (`CorpusFile.render`, which escapes through `Canonical.write Rendering.CrossHost`); every self-enumerated manifest is the
//  one schema (`CorpusManifest`); and every committed file is held to what its row renders by ONE
//  reading (`CorpusFile.firstDifference`), in the one test below. The test runner dispatches
//  `--emit <family>` over the table (`Program.fs`), the `--emit-<family>` spellings kept as aliases
//  because other records quote them. A new family is one row.
//
//  What a row does NOT hold is the family's ORACLE question — is what the file records still true of
//  the code? — which stays with the family's own tests, because each answers it in its own terms.
// ============================================================================

open System.IO
open Expecto
open Fuaran.Core

/// How a committed file is held to its render.
type Freshness =
    /// Byte for byte, CRLF read as LF.
    | Bytes
    /// By the workspace copy registry's fingerprint (`OwnedConformance.fingerprint`): line endings, a
    /// BOM, per-line trailing whitespace and trailing blank lines ignored.
    | Fingerprint

/// One family's exporter.
type Exporter =
    {
        /// The family's name, as `--emit <family>` takes it.
        Family: string
        /// The directory the committed files live in when no target is named.
        Root: unit -> string
        /// Every file the family writes under a directory, with the text it renders.
        Files: string -> (string * string) list
        /// A writer that does more than write `Files` (a restamp, a merge into a shared manifest);
        /// `None` writes each file as rendered.
        Write: (string -> unit) option
        /// How the committed files are held to their render.
        Freshness: Freshness
    }

let private twoFiles (vectors: string -> string) (manifest: string -> string) render renderManifest =
    fun dir -> [ vectors dir, render (); manifest dir, renderManifest () ]

/// The table.
let table: Exporter list =
    [ { Family = "laws"
        Root = OwnedConformance.root
        Files = LawVectorExport.emitted
        Write = Some LawVectorExport.write
        Freshness = Bytes }
      { Family = "apply"
        Root = OwnedConformance.root
        Files =
          twoFiles
              ApplyVectorExport.vectorsPath
              ApplyVectorExport.manifestPath
              ApplyVectorExport.renderVectors
              ApplyVectorExport.renderManifest
        Write = Some ApplyVectorExport.write
        Freshness = Bytes }
      { Family = "refusals"
        Root = OwnedConformance.root
        Files =
          fun dir ->
              twoFiles
                  RefusalVectorTests.RefusalCorpus.vectorsPath
                  RefusalVectorTests.RefusalCorpus.manifestPath
                  RefusalVectorTests.RefusalCorpus.render
                  RefusalVectorTests.RefusalCorpus.renderManifest
                  dir
              // Phase 551 — the seam-refusal family, beside the codec refusals in the same index.
              @ [ RefusalVectorTests.RefusalCorpus.seamPath dir, RefusalVectorTests.RefusalCorpus.renderSeam () ]
        Write = None
        Freshness = Bytes }
      { Family = "decode"
        Root = OwnedConformance.root
        Files =
          twoFiles
              DecodeLayerTests.DecodeRejectCorpus.vectorsPath
              DecodeLayerTests.DecodeRejectCorpus.manifestPath
              DecodeLayerTests.DecodeRejectCorpus.render
              DecodeLayerTests.DecodeRejectCorpus.renderManifest
        Write = None
        Freshness = Bytes }
      { Family = "escape"
        Root = OwnedConformance.root
        Files = fun dir -> [ StringEscapeTests.EscapeCorpus.path dir, StringEscapeTests.EscapeCorpus.render () ]
        Write = None
        Freshness = Bytes }
      { Family = "encoding"
        Root = OwnedConformance.root
        Files =
          fun dir -> [ EncodingProfileTests.EncodingCorpus.path dir, EncodingProfileTests.EncodingCorpus.render () ]
        Write = None
        Freshness = Bytes }
      // The roster's two generated artefacts (Phase 184): `docs/`, not `conformance/`, and held by
      // fingerprint as they always were. The measured `cases` column is one reference run per call.
      { Family = "families"
        Root = ConformanceFamiliesTests.Export.root
        Files =
          fun dir ->
              let measured = ConformanceFamiliesTests.Export.cases ()

              [ ConformanceFamiliesTests.Export.markdownPath dir, KitRoster.toMarkdownWith measured
                ConformanceFamiliesTests.Export.jsonPath dir, KitRoster.toJsonWith measured ]
        Write = None
        Freshness = Fingerprint } ]

/// The row for a family name.
let tryFind (family: string) : Exporter option =
    table |> List.tryFind (fun e -> e.Family = family)

/// Write a row's files under `dir` (its own `Root` when none is named), returning the paths written.
/// Every file is written with LF endings whatever the platform: the corpus is byte-compared by hosts
/// on three operating systems.
let write (e: Exporter) (dir: string option) : string list =
    let dir = defaultArg dir (e.Root())
    let files = e.Files dir

    match e.Write with
    | Some write -> write dir
    | None ->
        for path, text in files do
            Directory.CreateDirectory(Path.GetDirectoryName path) |> ignore
            File.WriteAllText(path, text)

    files |> List.map fst

/// The ONE freshness reading of a row's committed files: one finding per file that is absent or
/// differs from what the row renders, naming the first line that differs and the command that
/// rewrites it.
let findings (e: Exporter) : string list =
    let remedy =
        sprintf "re-run `dotnet run --project tests/Fuaran.Core.Tests -- --emit %s` and commit the result" e.Family

    [ for path, rendered in e.Files(e.Root()) do
          if not (File.Exists path) then
              yield sprintf "%s does not exist — %s" path remedy
          else
              let committed = File.ReadAllText path

              let reading =
                  match e.Freshness with
                  | Bytes -> CorpusFile.firstDifference committed rendered
                  | Fingerprint ->
                      CorpusFile.firstDifference
                          (OwnedConformance.fingerprint committed)
                          (OwnedConformance.fingerprint rendered)

              match reading with
              | None -> ()
              | Some d ->
                  yield
                      sprintf
                          "%s is not what the `%s` row renders: line %d reads %A where the render has %A — %s"
                          path
                          e.Family
                          d.Line
                          d.Committed
                          d.Rendered
                          remedy ]

[<Tests>]
let tests =
    testList
        "Exporters"
        [ for e in table do
              yield
                  testCase (sprintf "the committed `%s` files are what the row renders" e.Family)
                  <| fun _ ->
                      match findings e with
                      | [] -> ()
                      | found -> failtest (String.concat "\n" found)

          yield
              testCase "go-red: a perturbed committed file is named, with its first differing line"
              <| fun _ ->
                  let rendered = "{\n  \"a\": 1,\n  \"b\": [\n  ]\n}\n"

                  Expect.isNone (CorpusFile.firstDifference rendered rendered) "a file equal to its render is fresh"

                  Expect.isNone
                      (CorpusFile.firstDifference (rendered.Replace("\n", "\r\n")) rendered)
                      "CRLF line endings are the one difference the reading forgives"

                  Expect.equal
                      (CorpusFile.firstDifference (rendered.Replace("1", "2")) rendered)
                      (Some
                          { Line = 2
                            Committed = "  \"a\": 2,"
                            Rendered = "  \"a\": 1," })
                      "a moved byte is named by its line"

                  Expect.equal
                      (CorpusFile.firstDifference (rendered.TrimEnd('\n')) rendered)
                      (Some
                          { Line = 6
                            Committed = "<absent>"
                            Rendered = "" })
                      "a lost trailing newline is a difference, not forgiven"

          yield
              testCase
                  "the one writer lays a document out a member and an item per line, escaping through Canonical.write Rendering.CrossHost"
              <| fun _ ->
                  let doc =
                      { Head = [ "family", CorpusFile.str "x\n\"y\""; "count", CorpusFile.num 2 ]
                        Items = "vectors"
                        Rows =
                          [ CorpusCell.Row [ "id", CorpusFile.str "a"; "input", CorpusCell.Row [] ]
                            CorpusCell.Value(JObj [ "b", JInt 1; "a", JInt 2 ])
                            CorpusCell.List [ CorpusFile.str "p"; CorpusFile.num 3 ] ] }

                  Expect.equal
                      (CorpusFile.render doc)
                      ("{\n  \"family\": \"x\\u000a\\\"y\\\"\",\n  \"count\": 2,\n  \"vectors\": [\n"
                       + "    { \"id\": \"a\", \"input\": {} },\n    {\"a\":2,\"b\":1},\n    [\"p\", 3]\n  ]\n}\n")
                      "author order and spacing in a Row, Canonical.write Rendering.CrossHost's sorted compact form in a Value"

                  Expect.equal
                      (CorpusFile.render { doc with Rows = [] })
                      "{\n  \"family\": \"x\\u000a\\\"y\\\"\",\n  \"count\": 2,\n  \"vectors\": [\n  ]\n}\n"
                      "an empty array closes on the next line"

          yield
              testCase "every family is one row, named once"
              <| fun _ ->
                  let names = table |> List.map _.Family
                  Expect.equal (List.distinct names) names "no family has two rows" ]
