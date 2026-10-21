module Fuaran.Core.Tests.Exporters

// ============================================================================
//  The exporter table (Phase 508): one row per family this repository emits as a committed file.
//
//  Seven `--emit-*` verbs grew one mechanism each — three JSON writers, a freshness equality per
//  family, and two manifest shapes. They are rows now. Every row renders through the one writer
//  (`CorpusFile.render`, which escapes through `Canonical.write Rendering.CrossHost`); every self-enumerated manifest is the
//  one schema (`CorpusManifest`); and every committed file is held to what its row renders by ONE
//  reading (`CorpusFile.firstDifference`), in the `Exporters` family (`ExportersTests`). The table
//  lives in the fixtures library since Phase 532, and the tool (`tools/Fuaran.Core.Tools`) is its
//  one writer: `render <family> [<dir>]`. A new family is one row.
//
//  What a row does NOT hold is the family's ORACLE question — is what the file records still true of
//  the code? — which stays with the family's own tests, because each answers it in its own terms.
//
//  The table is a function of ONE input the library cannot compute: the `families` row's measured
//  `cases` column is the conformance reference run, whose witnesses are the suite's own (Phase 196).
//  The tool and the suite both hand it the same function (`ConformanceVacuityTests.cases`).
// ============================================================================

open System.IO
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

/// The table, given the reference run that measures the `families` row's `cases` column.
let tableWith (measuredCases: unit -> (string * CaseCount) list) : Exporter list =
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
                  RefusalCorpus.vectorsPath
                  RefusalCorpus.manifestPath
                  RefusalCorpus.render
                  RefusalCorpus.renderManifest
                  dir
              // Phase 551 — the seam-refusal family, beside the codec refusals in the same index.
              @ [ RefusalCorpus.seamPath dir, RefusalCorpus.renderSeam () ]
        Write = None
        Freshness = Bytes }
      { Family = "decode"
        Root = OwnedConformance.root
        Files =
          twoFiles
              DecodeRejectCorpus.vectorsPath
              DecodeRejectCorpus.manifestPath
              DecodeRejectCorpus.render
              DecodeRejectCorpus.renderManifest
        Write = None
        Freshness = Bytes }
      { Family = "escape"
        Root = OwnedConformance.root
        Files = fun dir -> [ EscapeCorpus.path dir, EscapeCorpus.render () ]
        Write = None
        Freshness = Bytes }
      { Family = "encoding"
        Root = OwnedConformance.root
        Files = fun dir -> [ EncodingCorpus.path dir, EncodingCorpus.render () ]
        Write = None
        Freshness = Bytes }
      // The roster's two generated artefacts (Phase 184): `docs/`, not `conformance/`, and held by
      // fingerprint as they always were. The measured `cases` column is one reference run per call.
      { Family = "families"
        Root = FamiliesExport.root
        Files =
          fun dir ->
              let measured = measuredCases ()

              [ FamiliesExport.markdownPath dir, KitRoster.toMarkdownWith measured
                FamiliesExport.jsonPath dir, KitRoster.toJsonWith measured ]
        Write = None
        Freshness = Fingerprint } ]

/// The row for a family name.
let tryFind (table: Exporter list) (family: string) : Exporter option =
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
    let remedy = sprintf "re-run `%s` and commit the result" (Tool.render e.Family)

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

              let shown =
                  function
                  | Some line -> sprintf "%A" line
                  | None -> "nothing (past the end)"

              match reading with
              | None -> ()
              | Some d ->
                  yield
                      sprintf
                          "%s is not what the `%s` row renders: line %d reads %s where the render has %s — %s"
                          path
                          e.Family
                          d.Line
                          (shown d.Committed)
                          (shown d.Rendered)
                          remedy ]
