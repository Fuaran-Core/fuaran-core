namespace Fuaran.Core

/// The words `Rejection.explain` speaks in (Phase 315) — what the domain calls a node and its root,
/// so the same explainer serves a document ("block", "document root") and a sheet ("cell",
/// "workbook"). `RejectionNouns.generic` is "node" / "root".
type RejectionNouns =
    {
        /// The word for one node. The sentences put a fixed `a` / `no` before it and pluralise it by
        /// appending `s`, so pick a word that reads correctly both ways.
        Node: string
        /// The word for the tree's root, spoken after `the`.
        Root: string
    }

/// The stock nouns.
[<RequireQualifiedAccess>]
module RejectionNouns =

    /// "node" / "root".
    let generic: RejectionNouns = { Node = "node"; Root = "root" }
