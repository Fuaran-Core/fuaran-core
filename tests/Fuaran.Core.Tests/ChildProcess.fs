module Fuaran.Core.Tests.ChildProcess

open System
open System.Diagnostics
open System.Text
open System.Text.RegularExpressions

// ---------------------------------------------------------------------------
// Spawning a child whose stdout the cross-host suites compare byte-for-byte.
//
// `Process.StandardOutput` decodes the pipe with the PARENT's
// `Console.OutputEncoding` unless told otherwise. On Windows that is the
// console's code page, so under an OEM page (CP850) the child's UTF-8 `é`
// (C3 A9) decodes to `├⌐` and every vector carrying a non-ASCII character
// diverges from the in-process interpreter leg.
//
// That made the green gate a function of the console the runner happened to
// inherit rather than of the code: the Phase 316 and Phase 698 sweeps passed
// under `dotnet run` and failed when the built test dll was invoked directly.
//
// Both children the suite spawns — `node` and `dotnet fsi` — write UTF-8 to a
// redirected stream, so the decode is pinned here instead of left ambient.
// Every redirected spawn goes through this one function deliberately: a site
// that omits the setting still passes on the machine that wrote it, so the
// omission is invisible exactly where it would be caught.
// ---------------------------------------------------------------------------

let private utf8 = UTF8Encoding false

/// A `ProcessStartInfo` with both output streams redirected and their decoding
/// pinned to UTF-8, independent of the console's code page.
let redirected (fileName: string) (arguments: string) =
    let psi = ProcessStartInfo(fileName, arguments)
    psi.RedirectStandardOutput <- true
    psi.RedirectStandardError <- true
    psi.UseShellExecute <- false
    psi.StandardOutputEncoding <- utf8
    psi.StandardErrorEncoding <- utf8
    psi

/// `git <arguments>` run in `workingDir` through `redirected`: its standard output, or why git
/// could not say — a non-zero exit (with what git wrote), or git not runnable at all. The ONE git
/// helper the surface and corpus tests share (Phase 299): the public-surface and wire-surface
/// baselines and the sibling-corpus resolver each carried a copy of this body until then.
let git (workingDir: string) (arguments: string) : Result<string, string> =
    try
        let psi = redirected "git" arguments
        psi.WorkingDirectory <- workingDir
        use p = Process.Start psi
        let out = p.StandardOutput.ReadToEnd()
        let err = p.StandardError.ReadToEnd()
        p.WaitForExit()

        if p.ExitCode <> 0 then
            Error(sprintf "`git %s` exited %d: %s" arguments p.ExitCode ((err + out).Trim()))
        else
            Ok out
    with e ->
        Error(sprintf "`git %s` could not be run: %s" arguments e.Message)

/// The repository's RELEASE TAGS: the one reader every tag-reading family goes through (Phase 499).
///
/// Six families read tags: the release record, the package roster's draft-slot and per-tag checks,
/// the README's per-release claims, the 1.0 laws, and the public and wire surfaces' since-tag
/// reports. Until Phase 499 the release record read `git tag --list --merged HEAD` and the other
/// five read `git tag --list`, so on a branch behind the newest tag they disagreed about what "the
/// newest tag" was: the release record read the branch's own history while the rest read a tag the
/// branch does not contain, which is how two release-record tests went red while every other family
/// stayed green (Phase 475). Every family now reads the tags in HEAD's history, so "since the newest
/// tag" means since the newest tag this tree descends from, whichever family says it.
///
/// Only a plain `vX.Y.Z` tag is read. A pre-release spelling, the disposable
/// `v0.0.0-gate-probe-<date>` tag among them (RELEASING.md, "Exercising the release gates"), names
/// no released slot, and a tag shape this does not parse is out of frame rather than reported: the
/// alternative is reddening on someone else's tagging convention. `GateProbeTagTests` holds both.
[<RequireQualifiedAccess>]
module Tags =

    let private releaseRe =
        Regex(@"^v([0-9]+)\.([0-9]+)\.([0-9]+)$", RegexOptions.Compiled)

    /// `(major, minor, patch)` for a plain release tag, else `None`. Ordering over the triple is
    /// NUMERIC: `0.9.0` sorts below `0.25.0`, which a comparison of the tag strings gets backwards.
    let version (tag: string) : (int * int * int) option =
        let m = releaseRe.Match tag

        if not m.Success then
            None
        else
            match
                Int32.TryParse m.Groups[1].Value, Int32.TryParse m.Groups[2].Value, Int32.TryParse m.Groups[3].Value
            with
            | (true, a), (true, b), (true, c) -> Some(a, b, c)
            | _ -> None

    /// The release tags among the lines of a `git tag` listing; every other line is dropped.
    let ofListing (listing: string) : Set<string> =
        listing.Split('\n')
        |> Seq.map _.Trim()
        |> Seq.filter (version >> Option.isSome)
        |> Set.ofSeq

    /// The newest release tag among `tags`, by version; `None` when none is a release tag.
    let newest (tags: seq<string>) : string option =
        tags
        |> Seq.choose (fun t -> version t |> Option.map (fun v -> v, t))
        |> Seq.sortBy fst
        |> Seq.tryLast
        |> Option.map snd

    /// The release tags in the history of `root`'s HEAD. `--merged HEAD` includes a tag on HEAD
    /// itself, which is the release commit's own case in the publish workflow. An `Error` is git's
    /// (no git, or no repository: the source-archive case); an EMPTY set is a clone holding no
    /// release tag in its history, and what that means is each family's to say.
    let released (root: string) : Result<Set<string>, string> =
        git root "tag --list --merged HEAD" |> Result.map ofListing
