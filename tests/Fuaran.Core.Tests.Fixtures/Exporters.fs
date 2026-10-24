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
        /// The same files rendered with an explicit version stamp, for a family whose files carry
        /// one (`laws`, its `kitVersion`): opening a version slot stamps the version it opens, which
        /// is not the version the kit was built at (Phase 560). `None` for a family whose bytes
        /// carry no version, so its render is the same at every version.
        FilesAt: (string -> string -> (string * string) list) option
        /// A writer that does more than write the files it is handed (a restamp, a merge into a
        /// shared manifest), given the target directory and exactly the files to write there;
        /// `None` writes each file as rendered.
        Write: (string -> (string * string) list -> unit) option
        /// The SHARED indexes under a directory: files `Write` merges this family's rows into beside
        /// rows other producers own, rather than writing whole. A corpus copy of one is therefore not
        /// a copy of this repository's file, and the projection registry may declare only the ROW
        /// this repository owns in it (`rowIn`), never the file (`copies`) — DECISIONS.md D211.
        SharedIndexes: string -> string list
        /// How the committed files are held to their render.
        Freshness: Freshness
    }

let private twoFiles (vectors: string -> string) (manifest: string -> string) render renderManifest =
    fun dir -> [ vectors dir, render (); manifest dir, renderManifest () ]

/// This repository's own law-family index (Phase 564), at the family's home and nowhere else: under
/// any other directory `laws/manifest.json` is the corpus's shared index, which the law writer
/// restamps and never replaces, so the own index is not among the files a write there is handed.
let private ownLawIndex (dir: string) : (string * string) list =
    if
        System.String.Equals(
            Path.GetFullPath(dir).TrimEnd('/', '\\'),
            Path.GetFullPath(OwnedConformance.root ()).TrimEnd('/', '\\'),
            System.StringComparison.OrdinalIgnoreCase
        )
    then
        [ LawVectorExport.manifestPath dir, LawVectorExport.renderOwnManifest () ]
    else
        []

/// The table, given the reference run that measures the `families` row's `cases` column.
let tableWith (measuredCases: unit -> (string * CaseCount) list) : Exporter list =
    [ { Family = "laws"
        Root = OwnedConformance.root
        Files = fun dir -> LawVectorExport.emitted dir @ ownLawIndex dir
        FilesAt = Some(fun stamp dir -> LawVectorExport.emittedAt stamp dir @ ownLawIndex dir)
        Write = Some LawVectorExport.writeFiles
        // The corpus's `laws/manifest.json` indexes compute's families too; `write` restamps only
        // the rows of the families written here (Phase 394).
        SharedIndexes = fun dir -> [ LawVectorExport.manifestPath dir ]
        Freshness = Bytes }
      { Family = "apply"
        Root = OwnedConformance.root
        Files =
          twoFiles
              ApplyVectorExport.vectorsPath
              ApplyVectorExport.manifestPath
              ApplyVectorExport.renderVectors
              ApplyVectorExport.renderManifest
        FilesAt = None
        // The apply writer writes its vectors and merges its manifest row as ONE act, so it is
        // handed both of its files or none: the corpus declares both (a copy and a `rowIn`), and a
        // registry that stopped declaring one is refused here rather than half-honoured.
        Write =
          Some(fun dir files ->
              if List.length files <> 2 then
                  failwithf
                      "render apply: %d of its 2 files are declared under '%s'; it writes both or neither — nothing was written"
                      (List.length files)
                      dir

              ApplyVectorExport.write dir)
        // The corpus's `apply/manifest.json` carries other producers' families beside this one;
        // `write` merges this family's row and keeps theirs (`ApplyVectorExport.mergeManifest`).
        SharedIndexes = fun dir -> [ ApplyVectorExport.manifestPath dir ]
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
        FilesAt = None
        Write = None
        SharedIndexes = fun _ -> []
        Freshness = Bytes }
      { Family = "decode"
        Root = OwnedConformance.root
        Files =
          twoFiles
              DecodeRejectCorpus.vectorsPath
              DecodeRejectCorpus.manifestPath
              DecodeRejectCorpus.render
              DecodeRejectCorpus.renderManifest
        FilesAt = None
        Write = None
        SharedIndexes = fun _ -> []
        Freshness = Bytes }
      { Family = "escape"
        Root = OwnedConformance.root
        // Phase 568 — the table and its self-enumerated index, held byte for byte.
        Files =
          fun dir ->
              [ EscapeCorpus.path dir, EscapeCorpus.render ()
                EscapeCorpus.manifestPath dir, EscapeCorpus.renderManifest () ]
        FilesAt = None
        Write = None
        SharedIndexes = fun _ -> []
        Freshness = Bytes }
      { Family = "encoding"
        Root = OwnedConformance.root
        // Phase 568 — the table and its self-enumerated index, held byte for byte.
        Files =
          fun dir ->
              [ EncodingCorpus.path dir, EncodingCorpus.render ()
                EncodingCorpus.manifestPath dir, EncodingCorpus.renderManifest () ]
        FilesAt = None
        Write = None
        SharedIndexes = fun _ -> []
        Freshness = Bytes }
      // Phase 549 — the four structural families over the skeleton witness (`chain`, `dag`, `merge`,
      // `diff`), one row each: a vector file and its self-enumerated manifest, held byte for byte.
      for f in StructuralVectorExport.families do
          { Family = f.Name
            Root = OwnedConformance.root
            Files =
              twoFiles
                  (StructuralVectorExport.vectorsPath f)
                  (StructuralVectorExport.manifestPath f)
                  (fun () -> StructuralVectorExport.renderVectors f)
                  (fun () -> StructuralVectorExport.renderManifest f)
            FilesAt = None
            Write = None
            SharedIndexes = fun _ -> []
            Freshness = Bytes }
      // Phase 483 — the host-premise probes: one vector file per `host-premise` row and their
      // self-enumerated manifest, held byte for byte.
      { Family = "premises"
        Root = OwnedConformance.root
        Files = PremiseProbeExport.files
        FilesAt = None
        Write = None
        SharedIndexes = fun _ -> []
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
        FilesAt = None
        Write = None
        SharedIndexes = fun _ -> []
        Freshness = Fingerprint } ]

/// The row for a family name.
let tryFind (table: Exporter list) (family: string) : Exporter option =
    table |> List.tryFind (fun e -> e.Family = family)

/// Where a row's files are written (Phase 560).
type Target =
    /// The family's home in this repository (its `Root`): it gets every file the family emits.
    | Own of dir: string
    /// Any other directory — a checkout of the shared corpus. It gets only the files the projection
    /// registry declares there for this family: its whole-file `copies`, the shared indexes its rows
    /// are merged into (`rowIn`, and the row's own `SharedIndexes`, which a file this repository does
    /// not carry, such as the corpus's `laws/manifest.json`, can only be declared by). A file the
    /// family emits and no record declares never lands outside this repository: `render laws <corpus>`
    /// wrote `laws/canonical-form-laws.json` into every corpus checkout until this phase.
    | Elsewhere of dir: string

let private fullPath (p: string) = Path.GetFullPath(p).TrimEnd('/', '\\')

/// The target `dir` names for a row: its own home when none is named or `dir` IS that home.
let targetOf (e: Exporter) (dir: string option) : Target =
    match dir with
    | None -> Own(e.Root())
    | Some d when System.String.Equals(fullPath d, fullPath (e.Root()), System.StringComparison.OrdinalIgnoreCase) ->
        Own d
    | Some d -> Elsewhere d

/// The paths, relative to a checkout and forward-slashed, the registry `rows` declare for `e`'s
/// family there: every copy and every `rowIn` of a row the family renders.
let declared (rows: Approval.Projection list) (e: Exporter) : Set<string> =
    rows
    |> List.filter (fun r -> r.Render = Some e.Family)
    |> List.collect (fun r -> r.Copies @ r.RowIn)
    |> List.map _.Path
    |> Set.ofList

/// The files a write to `target` lands, with the text each gets: everything the row renders at its
/// own home, only what `rows` declares anywhere else. `stamp` renders at an explicit version where
/// the row's files carry one.
let filesFor (rows: Approval.Projection list) (stamp: string option) (e: Exporter) (target: Target) =
    let dir =
        match target with
        | Own d
        | Elsewhere d -> d

    let rendered =
        match stamp, e.FilesAt with
        | Some s, Some at -> at s dir
        | _ -> e.Files dir

    match target with
    | Own _ -> rendered
    | Elsewhere _ ->
        let keep = declared rows e
        let shared = e.SharedIndexes dir |> List.map fullPath |> Set.ofList

        let relative (p: string) =
            Path.GetRelativePath(dir, p).Replace('\\', '/')

        rendered
        |> List.filter (fun (p, _) -> keep.Contains(relative p) || shared.Contains(fullPath p))

/// Write a row's files to `target` as `filesFor` selects them, returning the paths written. Every
/// file is written with LF endings whatever the platform: the corpus is byte-compared by hosts on
/// three operating systems.
let writeTo (rows: Approval.Projection list) (stamp: string option) (e: Exporter) (target: Target) : string list =
    let dir =
        match target with
        | Own d
        | Elsewhere d -> d

    let files = filesFor rows stamp e target

    match e.Write with
    | Some write -> write dir files
    | None ->
        for path, text in files do
            Directory.CreateDirectory(Path.GetDirectoryName path) |> ignore
            File.WriteAllText(path, text)

    files |> List.map fst

/// Write a row's files under `dir` (its own `Root` when none is named), against the committed
/// projection registry, at this kit's own version: `render <family> [<dir>]`.
let write (e: Exporter) (dir: string option) : string list =
    writeTo Approval.registry.Value None e (targetOf e dir)

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
