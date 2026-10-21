namespace Fuaran.Core.Tests

open System
open System.Diagnostics
open System.IO
open System.Text
open System.Text.RegularExpressions

// ---------------------------------------------------------------------------
// Phase 502 — the suite's ONE child-process runner, its ONE release-tag reader and its ONE
// repository-root resolver. `ApprovalTests` holds the first (no `Process.Start` outside this
// file); `GateProbeTagTests` holds the second.
//
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
// Every child the suite spawns — `git`, `node`, `pwsh` and `dotnet` — writes
// UTF-8 to a redirected stream, so the decode is pinned here instead of left
// ambient. Both pipes are drained CONCURRENTLY: a child that fills the pipe not
// being read blocks on its write while the parent blocks reading the other one,
// a deadlock that thirty hand-written spawn sites each risked until Phase 502
// routed them here.
// ---------------------------------------------------------------------------

/// The suite's one child-process runner.
[<RequireQualifiedAccess>]
module ChildProcess =

    /// How a command's arguments are handed over: one command line, split by the runtime's own
    /// rules, or an argument vector passed element for element (no quoting to get wrong).
    type Arguments =
        | Line of string
        | Argv of string list

    /// One child to run: the program, its arguments, where, and any variables set in its environment
    /// on top of the inherited one.
    type Command =
        {
            File: string
            Arguments: Arguments
            WorkingDirectory: string option
            Environment: (string * string) list
            /// Text written to the child's standard input as UTF-8 (no byte-order mark), which is then
            /// closed; `None` leaves standard input unredirected.
            Stdin: string option
        }

    /// What a child that ran said: its exit code and both pipes, each decoded as UTF-8 and whole, with
    /// standard output's raw bytes beside its text (a byte-order-mark check reads what the child wrote,
    /// not what a decoder made of it).
    type Output =
        { Exit: int
          Stdout: string
          StdoutBytes: byte[]
          Stderr: string }

    /// `file arguments`, as one command line.
    let command (file: string) (arguments: string) : Command =
        { File = file
          Arguments = Line arguments
          WorkingDirectory = None
          Environment = []
          Stdin = None }

    /// `file` with an argument vector.
    let argv (file: string) (arguments: string list) : Command =
        { command file "" with
            Arguments = Argv arguments }

    /// The same command, run in `dir`.
    let inDir (dir: string) (c: Command) : Command = { c with WorkingDirectory = Some dir }

    /// The same command, with `name` set to `value` in the child's environment.
    /// The same command, with `text` on its standard input.
    let withStdin (text: string) (c: Command) : Command = { c with Stdin = Some text }

    let withEnv (name: string) (value: string) (c: Command) : Command =
        { c with
            Environment = c.Environment @ [ name, value ] }

    let private utf8 = UTF8Encoding false

    let private describe (c: Command) =
        match c.Arguments with
        | Line "" -> c.File
        | Line a -> c.File + " " + a
        | Argv a -> String.concat " " (c.File :: a)

    /// Run `c` to completion. `Ok` whenever the child RAN, whatever its exit code: the code is the
    /// caller's to read, since several questions answer by it (`merge-base --is-ancestor`, a skip
    /// on a missing tool). `Error` only when the child could not be STARTED (the program is not on
    /// `PATH`, say), with the reason; a fault after the start is not a missing tool and is raised.
    let run (c: Command) : Result<Output, string> =
        let psi =
            match c.Arguments with
            | Line a -> ProcessStartInfo(c.File, a)
            | Argv a ->
                let psi = ProcessStartInfo c.File

                for x in a do
                    psi.ArgumentList.Add x

                psi

        psi.RedirectStandardOutput <- true
        psi.RedirectStandardError <- true
        psi.UseShellExecute <- false
        psi.StandardOutputEncoding <- utf8
        psi.StandardErrorEncoding <- utf8

        if c.Stdin.IsSome then
            psi.RedirectStandardInput <- true
            psi.StandardInputEncoding <- utf8

        c.WorkingDirectory |> Option.iter (fun d -> psi.WorkingDirectory <- d)

        for name, value in c.Environment do
            psi.Environment[name] <- value

        match
            (try
                Ok(Process.Start psi)
             with e ->
                 Error(sprintf "`%s` could not be run: %s" (describe c) e.Message))
        with
        | Error why -> Error why
        | Ok p ->
            use p = p
            use raw = new MemoryStream()
            let out = p.StandardOutput.BaseStream.CopyToAsync raw
            let err = p.StandardError.ReadToEndAsync()

            // Written only once both pipes are being drained, so a child answering as it reads
            // cannot block on a full stdout while the parent blocks writing its stdin.
            c.Stdin
            |> Option.iter (fun text ->
                p.StandardInput.Write text
                p.StandardInput.Close())

            p.WaitForExit()
            out.Wait()
            let bytes = raw.ToArray()

            // Decoded exactly as `StandardOutput.ReadToEnd()` would (UTF-8, a leading byte-order
            // mark consumed), from the bytes kept for the one family that asserts on them.
            use reader = new StreamReader(new MemoryStream(bytes), utf8, true)

            Ok
                { Exit = p.ExitCode
                  Stdout = reader.ReadToEnd()
                  StdoutBytes = bytes
                  Stderr = err.Result }

    /// `git <arguments>` run in `workingDir`: its standard output, or why git could not say — a
    /// non-zero exit (with what git wrote), or git not runnable at all. The question-and-answer
    /// form of `run` every one-line git question uses; a question that answers by exit code asks
    /// `run` directly.
    let git (workingDir: string) (arguments: string) : Result<string, string> =
        match run (command "git" arguments |> inDir workingDir) with
        | Error why -> Error why
        | Ok o when o.Exit <> 0 ->
            Error(sprintf "`git %s` exited %d: %s" arguments o.Exit ((o.Stderr + o.Stdout).Trim()))
        | Ok o -> Ok o.Stdout

    /// The repository's RELEASE TAGS: the one reader every tag-reading family goes through (Phase 499,
    /// Phase 502).
    ///
    /// Six families read tags: the release record, the package roster's draft-slot and per-tag checks,
    /// the README's per-release claims, the 1.0 laws, and the public and wire surfaces' since-tag
    /// reports. Until Phase 499 the release record read `git tag --list --merged HEAD` and the other
    /// five read `git tag --list`, so on a branch behind the newest tag they disagreed about what "the
    /// newest tag" was (Phase 475). Every family now reads the tags in HEAD's history, so "since the
    /// newest tag" means since the newest tag this tree descends from, whichever family says it.
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

        /// Why a clone holding no release tag at all has nothing for a tag-reading family to read.
        /// The prefix is what `GateProbeTagTests` and the families' skip lines are recognised by.
        [<Literal>]
        let NoReleaseTag =
            "this clone holds no `vX.Y.Z` release tag (a fork or a clone made without tags); fetch them with `git fetch --tags` to run the tag-reading checks"

        /// The release tags in the history of `root`'s HEAD. `--merged HEAD` includes a tag on HEAD
        /// itself, which is the release commit's own case in the publish workflow.
        ///
        /// Three answers (Phase 502, the tagless-clone contract `CONTRIBUTING.md` states):
        /// - `Error` with git's reason — no git, or no repository (the source-archive case);
        /// - `Error NoReleaseTag` — the clone holds no release tag ANYWHERE. Nothing about this tree
        ///   is wrong; the reading is "no released tag", and every family skips by name rather than
        ///   going red for a reason unrelated to the change under test;
        /// - `Ok tags` — the release tags in HEAD's history. An EMPTY set here means the clone holds
        ///   release tags and HEAD descends from none of them, which no checkout of this repository's
        ///   history can be: it is the shape a broken reader or a truncated history takes, and the
        ///   families that would otherwise pass vacuously are red on it.
        let released (root: string) : Result<Set<string>, string> =
            match git root "tag --list --merged HEAD" |> Result.map ofListing with
            | Ok merged when Set.isEmpty merged ->
                match git root "tag --list" |> Result.map ofListing with
                | Ok all when Set.isEmpty all -> Error NoReleaseTag
                | Ok _ -> Ok merged
                | Error why -> Error why
            | answer -> answer

/// The suite's one repository-root resolver (Phase 502): where this repository is, and where its
/// MAIN working tree is.
[<RequireQualifiedAccess>]
module Repo =

    /// `start` and its ancestors, nearest first, at most `budget` steps up.
    let ancestors (start: string) (budget: int) : string list =
        let rec go (dir: string) (n: int) (acc: string list) =
            if n < 0 || isNull dir then
                List.rev acc
            else
                match Directory.GetParent dir with
                | null -> List.rev (dir :: acc)
                | parent -> go parent.FullName (n - 1) (dir :: acc)

        go start budget []

    /// The repository root — the nearest directory holding `Fuaran.Core.slnx` — climbing from the
    /// current directory, then from the test binary; `None` when neither start is inside it. The
    /// climb deliberately reads no git: it must answer in a source archive too.
    let tryRoot () : string option =
        [ Directory.GetCurrentDirectory(); AppContext.BaseDirectory ]
        |> List.tryPick (fun start ->
            ancestors start 12
            |> List.tryFind (fun d -> File.Exists(Path.Combine(d, "Fuaran.Core.slnx"))))

    /// The repository root, or a failure naming what was looked for.
    let root () : string =
        tryRoot ()
        |> Option.defaultWith (fun () ->
            failwith
                "Repo.root: the Fuaran-Core root (Fuaran.Core.slnx) was not found above the current directory or the test binary")

    /// `relPath` under the repository root.
    let file (relPath: string) : string = Path.Combine(root (), relPath)

    /// This repository's MAIN working tree as git reports it, asked from `from`.
    ///
    /// `--git-common-dir` is the one question whose answer is the same from every worktree of a
    /// repository: a linked worktree's own `.git` is a FILE pointing into the main repository's
    /// administrative directory, and the common dir IS that directory — so its parent is the main
    /// working tree, whichever worktree asks. `--path-format=absolute` is asked for first because
    /// a bare `--git-common-dir` answers `.git`, relative to the invocation directory, when it is
    /// asked from the main tree itself; the second form resolves that by hand for a git too old to
    /// know the flag.
    let mainTreeFrom (from: string) : Result<string, string> =
        let ask args =
            ChildProcess.git from args |> Result.map _.Trim()

        let relative () =
            ask "rev-parse --git-common-dir"
            |> Result.map (fun p ->
                if Path.IsPathRooted p then
                    p
                else
                    Path.GetFullPath(Path.Combine(from, p)))

        let commonDir =
            match ask "rev-parse --path-format=absolute --git-common-dir" with
            | Ok p when not (String.IsNullOrWhiteSpace p) -> Ok p
            | _ -> relative ()

        commonDir
        |> Result.bind (fun p ->
            match Directory.GetParent(p.TrimEnd('/', '\\')) with
            | null -> Error(sprintf "the git common directory '%s' has no parent working tree" p)
            | parent -> Ok parent.FullName)

    /// This repository's main working tree, asked from the repository root.
    let mainTree () : Result<string, string> = mainTreeFrom (root ())
