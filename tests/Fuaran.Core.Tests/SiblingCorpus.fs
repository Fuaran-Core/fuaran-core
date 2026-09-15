module Fuaran.Core.Tests.SiblingCorpus

open System
open System.Diagnostics
open System.IO
open Fuaran.Core

// ---------------------------------------------------------------------------
// Where the shared wire-format conformance corpus is, and what happens when it
// is not there.
//
// Phase 130. Two suites certify against that corpus — the `laws/` transform
// vectors (LawVectorTests) and the `nodes/` drift guard (IdlSpikeTests) — and
// each used to find it by CLIMBING from `Directory.GetCurrentDirectory()` and
// `AppContext.BaseDirectory`, then skipping by name when the climb found
// nothing. Both halves of that were wrong, and they compounded.
//
//   * The CLIMB starts wherever the binary happens to be running. From this
//     repository's main working tree it reaches the corpus checked out
//     alongside; from a LINKED WORKTREE of the same repository it never does,
//     because a worktree sits somewhere else entirely. So the same commit
//     either certified against the corpus or did not, decided by which
//     checkout ran it.
//   * The SKIP made that invisible. Four worktree gates reported green over a
//     corpus none of them had read, and the first run that actually compared
//     was one in the main tree — where the comparison failed, hours later,
//     attached to a change that had not caused it.
//
// So the anchor is the repository's MAIN working tree, as git itself reports it
// (`rev-parse --git-common-dir`, whose parent is the main tree whichever
// worktree asks the question), and an absent corpus FAILS, naming every path
// tried and the remedy. A skip is still reachable — the corpus is a separate
// repository and a contributor on a bare clone may legitimately not have it —
// but only by ASKING for it, and the skip then prints the variable by name and
// says that nothing was compared.
//
// Phase 172 inverted which side is asked for. The families Core itself EMITS —
// `laws/transform-laws.json` and `apply/` — are committed in this repository
// (`conformance/`, see OwnedConformance) and the default suite reads them from
// there, so the suite is self-contained and a machine holding only this
// repository is green. What still wants the LIVE corpus is a different question
// — are the corpus's copies of those files fresh, and does the model still agree
// with production over the domain's own fixture pools (`nodes/`, `ops/`, `dag/`,
// `idl.json`) — and that leg is OPT-IN: `FUARAN_CORE_CORPUS_FRESHNESS=1` asks for
// it (CI does), and once asked for, an absent corpus FAILS exactly as Phase 130
// decided. D31 is preserved on the leg it was written for rather than imposed on
// a suite that no longer needs the corpus to certify Core's own contracts. The
// old blanket opt-out `FUARAN_CORE_SKIP_CORPUS` is retired: with nothing left to
// skip by default there is nothing for it to say.
// ---------------------------------------------------------------------------

/// Names an existing corpus checkout explicitly — the form CI uses, and the one the
/// documentation hands a contributor whose clone is not beside this repository.
[<Literal>]
let dirVariable = "FUARAN_CORE_CORPUS_DIR"

/// The documented opt-IN, and the ONLY way the live-corpus legs run. Set (to anything
/// non-blank) it asks for the comparison; unset, every leg that reads the shared corpus
/// reports itself NOT ASKED FOR, by this name, and the suite certifies Core's own committed
/// vectors alone. CI sets it, so every push still compares against the corpus at its `main`.
[<Literal>]
let askVariable = "FUARAN_CORE_CORPUS_FRESHNESS"

/// The corpus's own public repository — quoted in the remedy so the reader of a failing
/// gate is told the command rather than sent looking for it.
[<Literal>]
let cloneUrl = "https://github.com/fuaran-ui/fuaran-ui-specification.git"

/// The directory name every consumer of this corpus resolves it under. The directory name
/// is the interface; the repository name is not.
[<Literal>]
let directoryName = "wire-format-fixtures"

/// What to do about it — carried on every failure path, because the only useful thing a
/// resolution failure can do is tell you which directory to fix.
let remedy =
    String.concat
        "\n"
        [ sprintf "  Clone the corpus beside this checkout:  git clone %s %s" cloneUrl directoryName
          "  (run in the parent of the repository root, or inside the repository root — both are found),"
          sprintf "  or point %s at an existing clone." dirVariable
          sprintf
              "  This leg runs only when %s is set; unset it to run the self-contained suite, which reads Core's own committed conformance/ vectors and needs no corpus."
              askVariable ]

/// The three outcomes, with no fourth in which a leg quietly does nothing.
type Resolution =
    /// The corpus root — the directory holding `manifest.json` and the family directories.
    | Found of root: string
    /// The live-corpus leg was not asked for (`askVariable` unset); the reason names the
    /// variable and says that nothing was compared. Consumers SKIP on this, by name.
    | NotAsked of why: string
    /// Asked for, and no corpus. The reason names the paths tried and the remedy; consumers
    /// FAIL on this.
    | Absent of why: string

/// Why `root` is not the wire-format conformance corpus carrying `family`, or `None` when it
/// is.
///
/// Anchored on what the corpus's own `manifest.json` DECLARES rather than on a substring of
/// its prose — the Phase 129 lesson, that a containment probe cannot tell "this IS that" from
/// "this MENTIONS that". The corpus is a separate repository which declares no `kind` member,
/// so the identity available is the shape of the index it does declare: the `schema` and `idl`
/// documents it publishes, with the requested family directory beside them.
let fault (family: string) (root: string) : string option =
    let manifest = Path.Combine(root, "manifest.json")

    let expected =
        sprintf
            "the corpus root carries a manifest.json declaring \"schema\" and \"idl\", with a %s/ family beside it"
            family

    let read =
        try
            if File.Exists manifest then
                Ok(File.ReadAllText manifest)
            else
                Error "no manifest.json"
        with e ->
            Error("manifest.json could not be read: " + e.Message)

    match read with
    | Error why -> Some(sprintf "%s at '%s' (%s)" why manifest expected)
    | Ok text ->
        match Json.parse text with
        | Error e -> Some(sprintf "manifest.json at '%s' is not JSON (%s); %s" manifest e expected)
        | Ok(JObj fields) ->
            let declared name =
                fields
                |> List.exists (function
                    | k, JStr _ -> k = name
                    | _ -> false)

            match [ "schema"; "idl" ] |> List.filter (declared >> not) with
            | [] ->
                if Directory.Exists(Path.Combine(root, family)) then
                    None
                else
                    Some(sprintf "'%s' is the corpus, but it carries no %s/ directory" root family)
            | missing ->
                Some(
                    sprintf
                        "manifest.json at '%s' declares no %s member; %s"
                        manifest
                        (missing |> List.map (sprintf "\"%s\"") |> String.concat " / ")
                        expected
                )
        | Ok _ -> Some(sprintf "manifest.json at '%s' is not a JSON object; %s" manifest expected)

/// Run git in `workingDir`, with stdout decoded as UTF-8 rather than as whatever code page
/// the console handed us (`ChildProcess`'s reason for existing).
let private git (workingDir: string) (arguments: string) : Result<string, string> =
    try
        let psi = ChildProcess.redirected "git" arguments
        psi.WorkingDirectory <- workingDir
        use p = Process.Start psi
        let out = p.StandardOutput.ReadToEnd()
        let err = p.StandardError.ReadToEnd()
        p.WaitForExit()

        if p.ExitCode = 0 then
            Ok(out.Trim())
        else
            Error(sprintf "`git %s` exited %d: %s" arguments p.ExitCode ((err + out).Trim()))
    with e ->
        Error(sprintf "`git %s` could not be run: %s" arguments e.Message)

/// This repository's MAIN working tree as git reports it, asked from `from`.
///
/// `--git-common-dir` is the one question whose answer is the same from every worktree of a
/// repository: a linked worktree's own `.git` is a FILE pointing into the main repository's
/// administrative directory, and the common dir IS that directory — so its parent is the main
/// working tree, whichever worktree asks. `--path-format=absolute` is asked for first because
/// a bare `--git-common-dir` answers `.git`, relative to the invocation directory, when it is
/// asked from the main tree itself; the second form resolves that by hand for a git too old to
/// know the flag.
let mainWorkingTreeFrom (from: string) : Result<string, string> =
    let relative () =
        git from "rev-parse --git-common-dir"
        |> Result.map (fun p ->
            if Path.IsPathRooted p then
                p
            else
                Path.GetFullPath(Path.Combine(from, p)))

    let commonDir =
        match git from "rev-parse --path-format=absolute --git-common-dir" with
        | Ok p when not (String.IsNullOrWhiteSpace p) -> Ok p
        | _ -> relative ()

    commonDir
    |> Result.bind (fun p ->
        match Directory.GetParent(p.TrimEnd('/', '\\')) with
        | null -> Error(sprintf "the git common directory '%s' has no parent working tree" p)
        | parent -> Ok parent.FullName)

/// `dir` and its ancestors, nearest first, bounded.
let private ancestors (start: string) (budget: int) : string list =
    let rec go (dir: string) (n: int) (acc: string list) =
        if n < 0 || isNull dir then
            List.rev acc
        else
            match Directory.GetParent dir with
            | null -> List.rev (dir :: acc)
            | parent -> go parent.FullName (n - 1) (dir :: acc)

    go start budget []

/// Every place a corpus is looked for beneath one anchor, in the order tried: under the
/// language estate's own directory, which is where this workspace keeps it, and beside the
/// repository itself (a plain side-by-side clone, and the in-repository clone CI makes).
let private candidatesUnder (anchor: string) : string list =
    ancestors anchor 12
    |> List.collect (fun dir ->
        [ Path.Combine(dir, "Fuaran-UI", directoryName)
          Path.Combine(dir, directoryName) ])

/// The corpus root beneath `anchor`, or why not — with the anchoring itself decided by the
/// caller and described in `how`, so a failure says where it looked as well as what it wanted.
///
/// A directory of the right NAME that is not the corpus does not end the search: the next
/// candidate is tried, and only if none of them is the corpus are the faults reported
/// together. What is never done is passing over the whole set in silence.
let underAnchor (family: string) (anchor: string) (how: string) : Result<string, string> =
    let tried = candidatesUnder anchor
    let existing = tried |> List.filter Directory.Exists

    let listed (paths: string list) =
        paths |> List.map (fun p -> "\n  " + p) |> String.concat ""

    match existing |> List.tryFind (fun p -> (fault family p).IsNone) with
    | Some root -> Ok root
    | None ->
        match existing with
        | [] ->
            Error(
                sprintf
                    "the %s conformance corpus was not found — the %s/ comparison cannot run.\nAnchored at %s; tried:%s\n%s"
                    directoryName
                    family
                    how
                    (listed tried)
                    remedy
            )
        | present ->
            let faults =
                present
                |> List.map (fun p -> "\n  " + (fault family p |> Option.defaultValue "(no fault)"))
                |> String.concat ""

            Error(
                sprintf
                    "a directory named %s was found but is not the conformance corpus carrying %s/.\nAnchored at %s; found:%s\nand:%s\n%s"
                    directoryName
                    family
                    how
                    (listed present)
                    faults
                    remedy
            )

/// The corpus root as the ANCHORED lookup finds it from `from` — the mechanism the override
/// and the opt-out sit in front of. Public so the anchoring can be proved from a second
/// worktree of this repository without the environment deciding the answer.
let anchoredFrom (family: string) (from: string) : Result<string, string> =
    let anchor, how =
        match mainWorkingTreeFrom from with
        | Ok tree ->
            tree,
            sprintf
                "the repository's main working tree, '%s' (git rev-parse --git-common-dir, asked from '%s')"
                tree
                from
        | Error why ->
            let cwd = Directory.GetCurrentDirectory()

            cwd, sprintf "the current directory, '%s' — git could not say where the main working tree is (%s)" cwd why

    underAnchor family anchor how

/// The lookup itself, with the ask IMPLIED: the explicit directory when one is named, else the
/// anchored search. This is what a COMMAND uses — `--emit-fstar` reads the pinned corpus because
/// the operator ran it, and an invocation is its own ask — and what `resolveWith` puts the
/// opt-in gate in front of for the test legs.
let locateWith (dirValue: string option) (family: string) (from: string) : Result<string, string> =
    match dirValue with
    | Some ovr when not (String.IsNullOrWhiteSpace ovr) ->
        match fault family ovr with
        | None -> Ok ovr
        | Some why ->
            Error(
                sprintf
                    "%s is set to '%s', which is not the conformance corpus: %s.\nUnset it to anchor at the repository's main working tree instead.\n%s"
                    dirVariable
                    ovr
                    why
                    remedy
            )
    | _ -> anchoredFrom family from

/// The corpus this run certifies `family` against, or the reason there is none — decided from
/// the two variables' VALUES rather than the process environment, so the gate can be proved in
/// both directions without a test mutating the environment beside its neighbours.
///
/// The order is the point: the ask is read FIRST, and a leg that was not asked for consults
/// nothing — not the override, not git, not the filesystem — so a checkout with no corpus
/// anywhere is green by construction rather than by every candidate path happening to miss.
let resolveWith (askValue: string option) (dirValue: string option) (family: string) (from: string) : Resolution =
    match askValue with
    | Some asked when not (String.IsNullOrWhiteSpace asked) ->
        match locateWith dirValue family from with
        | Ok root -> Found root
        | Error why -> Absent why
    | _ ->
        NotAsked(
            sprintf
                "%s is not set — the live-corpus %s/ leg was NOT ASKED FOR and nothing was compared against the shared corpus; Core's own committed conformance/ vectors were certified by the default suite. Set %s=1 to run this leg (CI does)."
                askVariable
                family
                askVariable
        )

let private env (name: string) : string option =
    match Environment.GetEnvironmentVariable name with
    | null -> None
    | v -> Some v

/// `resolveWith` over the process environment.
let resolveFrom (family: string) (from: string) : Resolution =
    resolveWith (env askVariable) (env dirVariable) family from

/// Resolved with git asked from the test binary's own directory — the place the QUESTION is
/// asked from, never the place the search starts. `AppContext.BaseDirectory` sits inside
/// whichever worktree is running, which is exactly why it cannot be the anchor.
let resolve (family: string) : Resolution =
    resolveFrom family AppContext.BaseDirectory

/// The lookup with the ask implied, from the test binary's own directory — for commands.
let locate (family: string) : Result<string, string> =
    locateWith (env dirVariable) family AppContext.BaseDirectory
