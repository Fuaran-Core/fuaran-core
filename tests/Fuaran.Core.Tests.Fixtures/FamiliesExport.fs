namespace Fuaran.Core.Tests

/// The law-family roster's two generated artefacts (Phase 184): where they are written and where the
/// suite compares them. Rendered by `KitRoster.toMarkdownWith` / `toJsonWith` over the measured
/// `cases` column, which the reference run beside the vacuity family supplies.
module FamiliesExport =

    open System.IO

    let markdownPath (root: string) =
        Path.Combine(root, "conformance-families.md")

    let jsonPath (root: string) =
        Path.Combine(root, "conformance-families.json")

    /// The directory the two artefacts live in — this repository's own `docs/`, resolved through
    /// the same root marker every other owned artefact uses, so a linked worktree writes and reads
    /// its own copy.
    let root () = Snapshots.repoFile "docs"
