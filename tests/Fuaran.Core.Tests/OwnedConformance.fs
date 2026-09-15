module Fuaran.Core.Tests.OwnedConformance

open System.IO

// ---------------------------------------------------------------------------
// Where the conformance vectors THIS repository emits live, and the equality
// their published copies are held to.
//
// Phase 172. Two families in the shared wire-format corpus are Core's own
// generic contracts — `laws/transform-laws.json` and the `apply/` family — and
// until this phase they were emitted by Core, committed only in the corpus,
// and then read BACK by Core's own suites from `wire-format-fixtures/`. Since
// Phase 130 an absent corpus fails, so the generic spine's gate was red on a
// machine holding only this repository: "Core is generic" was true of the
// packages and false of the gate.
//
// So the source of truth is the committed `conformance/` directory at this
// repository's root, and the corpus carries a DECLARED COPY (`copies.json`
// beside it, on the estate's copy registry). The default suite reads from
// here and is self-contained; the corpus copy's freshness is a separate,
// opt-in question — see `SiblingCorpus`.
//
// Two roots, deliberately two modules: `SiblingCorpus` answers "where is the
// corpus another repository owns, and was this run asked to compare against
// it?"; this module answers "where is what THIS repository owns?". The first
// is anchored at git's main working tree and can be absent; this one is
// resolved from the repository's own root marker and is committed, so it is
// never absent on a checkout that builds.
// ---------------------------------------------------------------------------

/// The directory at the repository root that holds the families Core emits.
[<Literal>]
let dirName = "conformance"

/// The committed `conformance/` directory of the checkout this binary was built from —
/// resolved through the same root marker the snapshot store uses, so a linked worktree
/// reads ITS OWN copy rather than the main tree's.
let root () : string = Snapshots.repoFile dirName

/// Content equality insensitive to a UTF-8 BOM, line endings, per-line trailing whitespace
/// and trailing blank lines — the `fingerprint` check of the workspace copy registry
/// (`roadmapctl copies`), restated here so the in-suite freshness leg and the estate sweep
/// answer the same question with the same equality. A weaker equality than bytes, never a
/// fuzzy match: two texts with the same fingerprint differ only in what a checkout's
/// end-of-line policy is allowed to change.
let fingerprint (text: string) : string =
    let unbom = if text.StartsWith "﻿" then text.Substring 1 else text

    let lines =
        unbom.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')
        |> Array.map (fun l -> l.TrimEnd())

    let lastContent =
        match lines |> Array.tryFindIndexBack (fun l -> l <> "") with
        | Some i -> i
        | None -> -1

    String.concat "\n" lines[..lastContent]
