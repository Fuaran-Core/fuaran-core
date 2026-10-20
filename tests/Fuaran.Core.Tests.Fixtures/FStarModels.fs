module Fuaran.Core.Tests.FStarModels

// The GENERATED proof models (Phase 150; re-sourced by Phase 173): the certification set the F*
// backend is proved over, each vocabulary's model and proof script, and the writer. The tool's
// `render fstar` writes them; `IdlFStarTargetTests` (Proofs.Vocabulary) holds the committed files to a
// fresh generation from here.

open System.IO
open Fuaran.Core.Idl
open Fuaran.Core.Idl.Codegen.Types

let repoRoot = Path.GetDirectoryName(Snapshots.repoFile "Fuaran.Core.slnx")

let lf (s: string) = s.Replace("\r\n", "\n")

/// One generated model: the F* module name (which is also the file name under `proofs/`),
/// the vocabulary it is generated from, and the provenance its header names.
type Generated =
    { Module: string
      Idl: Idl
      Provenance: Provenance }

/// What every header says about the remedy, once — the command and the family that holds
/// the committed file to it. The origin paragraph of each provenance ends with this.
let remedyLines =
    [ "DO NOT EDIT: `dotnet run --project tests/Fuaran.Core.Tests -- --emit-fstar` regenerates"
      "it, and the `Proofs.Vocabulary` family holds this file to a fresh generation from that"
      "source — a vocabulary that moves without a regeneration is VOCABULARY DRIFT, and"
      "`proofs/check.ps1` names it." ]

/// What every header says about the claim, once. The model is a property of the BACKEND
/// over the certification set, which is the whole of the Phase 173 change: which
/// vocabulary the theorem is ABOUT.
let provesLines =
    [ "F* BACKEND (`Fuaran.Core.Idl.Codegen`'s `FStarTarget`) over the vocabulary the engine"
      "is certified on — the same set the F# and TypeScript backends are certified over — and"
      "not of any domain's vocabulary. A domain proves its own vocabulary in its own"
      "repository with the same generator; the UI vocabulary's model is `fuaran#1754`'s." ]

/// The certification set the F* backend is proved over, in the order the F# and TypeScript
/// backends meet it in `IdlCertificationTests`: the reference vocabulary carries the part of
/// the type model neither vendored sample uses, and the two samples carry the declared
/// NON-DEFAULT wire shape (bare-string discriminator, flat node envelope, declaration key
/// order) that the reference vocabulary, on `WireShape.Default`, does not reach.
let generated: Generated list =
    [ { Module = "Vocabulary"
        Idl = ReferenceIdl.refIdl
        Provenance =
          { Origin =
              [ "the engine's REFERENCE vocabulary — `tests/Fuaran.Core.Tests/ReferenceIdl.fs`, the"
                "domain-neutral `Idl` Phase 114 authored to reach the part of the type model no"
                "vendored sample uses (fuaran-core Phase 173, applying D14 to `proofs/`)." ]
              @ remedyLines
            Proves = provesLines } }
      { Module = "DocVocabulary"
        Idl = SecondDomainIdl.docIdl
        Provenance =
          { Origin =
              [ "the vendored SECOND-DOMAIN sample — `tests/Fuaran.Core.Tests/SecondDomainSpike.fs`,"
                "a document vocabulary with a corpus written outside this repository, on the"
                "declared non-default wire shape the reference vocabulary does not use"
                "(fuaran-core Phase 173, applying D14 to `proofs/`)." ]
              @ remedyLines
            Proves = provesLines } }
      { Module = "ScoreVocabulary"
        Idl = ScoreDomainIdl.scoreIdl
        Provenance =
          { Origin =
              [ "the vendored THIRD-DOMAIN sample — `tests/Fuaran.Core.Tests/ScoreDomainSpike.fs`,"
                "a notation vocabulary with a corpus written outside this repository: records and"
                "omit-at-default at scale, on the same declared non-default wire shape"
                "(fuaran-core Phase 173, applying D14 to `proofs/`)." ]
              @ remedyLines
            Proves = provesLines } } ]

let modelPath (g: Generated) =
    Path.Combine(repoRoot, "proofs", g.Module + ".fst")

/// The proof script beside a model: `<M>Proofs.fst`, opening `<M>`.
let proofsName (g: Generated) = g.Module + "Proofs"

let proofsPath (g: Generated) =
    Path.Combine(repoRoot, "proofs", proofsName g + ".fst")

/// The kinds a committed model covers: EVERY kind the backend can express, not the
/// `FStarTarget.proofKinds` slice. That rule is a cost control sized against a vocabulary whose
/// node envelope already carries dozens of declared types, where a kind reaching only those
/// costs one constructor and a kind bringing its own object is a measured decision (Phase 150,
/// over the UI vocabulary). Applied to the certification set it is the wrong instrument: the
/// reference vocabulary's envelope is two scalars, so the rule kept ONE of its five kinds and
/// dropped the four that carry the type-model remainder the vocabulary exists to reach —
/// checked before this was written, and the reason `selection` is not `proofKinds` here. The
/// cost of the whole set is measured in `proofs/modules.json`, where it is small enough that
/// there is nothing for a cost control to control. The rule itself is unchanged, because it is
/// the adopter's (`fuaran#1754`) instrument at the scale it was measured at.
let selection (idl: Idl) : string list =
    FStarTarget.partition idl |> List.filter _.Refusal.IsNone |> List.map _.Tag

/// Both generated files for one vocabulary, from one walk: the model and the theorems over it.
/// Phase 150 committed only the model, because the theorems did not discharge at the UI
/// vocabulary's scale; over the certification set they do (measured, `proofs/modules.json`),
/// so both are committed and both are held here.
let pair (g: Generated) : (string * Result<string, CodegenError>) list =
    let kinds = selection g.Idl

    [ modelPath g, FStarTarget.vocabularyModuleFrom g.Provenance g.Module g.Idl kinds
      proofsPath g, FStarTarget.proofsModuleFrom g.Provenance (proofsName g) g.Module g.Idl kinds ]

/// `render fstar` — rewrite every committed model and proof script from the certification set.
/// The command the generation diff names when it fails, and the only sanctioned way those files
/// change.
let emit () : int =
    let write (path: string) (result: Result<string, CodegenError>) =
        match result with
        | Error e ->
            eprintfn "render fstar: %s" (CodegenError.describe e)
            2
        | Ok text ->
            File.WriteAllText(path, lf text, System.Text.UTF8Encoding false)
            printfn "regenerated %s" path
            0

    generated
    |> List.fold
        (fun worst g ->
            let written =
                pair g |> List.map (fun (path, result) -> write path result) |> List.max

            printfn
                "%s: %d of %d kinds modelled; the rest are named in the emitted header"
                g.Module
                (selection g.Idl |> List.length)
                (List.length g.Idl.Kinds)

            max worst written)
        0
