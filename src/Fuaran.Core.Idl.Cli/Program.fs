/// The `fuaran-core-idl` command: the IDL stability classifier (`classify`), the F#
/// consequence table (`table`) and the proposal pricer (`spike-proposal`) over the
/// `Fuaran.Core.Idl.Diff` library, for repositories with no F# build of their own.
module Fuaran.Core.Idl.Cli.Program

open System.IO
open Fuaran.Core.Idl

// ---------------------------------------------------------------------------
// Phase 127 — `fuaran-core-idl`, the IDL stability classifier as a command.
//
// The classification is a library and stays one (`Fuaran.Core.Idl.Diff`). What
// was missing was a way for a repository that holds a committed `idl.json` pair,
// and no F# build of its own, to REACH it: the only entry point was a flag on
// this repo's own Expecto runner, which another repository cannot invoke at all.
//
// Two calling shapes, because a gate is in one of two positions:
//
//   • It does NOT know what to expect — it is classifying whatever the pull
//     brought — and wants to branch. It reads the exit code: 0 for anything a
//     consumer absorbs by repinning, 3 for a break, 4 for undecided.
//
//   • It DOES know — the change was deliberate and the author declared its class
//     — and wants an assertion that fails when the declaration and the artifact
//     disagree. `--expect <class>` exits 0 on a match and 1 on a mismatch,
//     printing both sides. That is the form that belongs in a `run.ps1`, because
//     it is the form that can go red for the right reason.
//
// Everything `classify` and `table` write goes to stdout, including refusals. A gate
// that captures the command's output gets the whole record in one stream, and nothing
// depends on how the calling shell treats a native command's stderr. `spike-proposal`
// (Phase 230: moved here from the repository's test runner, behaviour unchanged) is the
// operator-facing exception: its refusals stay on stderr, as they always were.
//
// FSharp.Core only — no argument-parsing dependency. The surface is three verbs and
// a few options; a parser combinator library for that would be a dependency every
// consumer of the tool inherits in exchange for nothing.
// ---------------------------------------------------------------------------

let private usage =
    let classes = Diff.allClasses |> List.map Diff.classLabel |> String.concat " | "

    "fuaran-core-idl — the IDL stability classifier over two `idl.json` revisions.\n"
    + "\n"
    + "USAGE\n"
    + "  fuaran-core-idl classify <before.json> <after.json> [--manifest <manifest.json>]\n"
    + "                                                      [--expect <class>]\n"
    + "                                                      [--support-before <support.json>\n"
    + "                                                       --support-after <support.json>]\n"
    + "  fuaran-core-idl table\n"
    + "  fuaran-core-idl spike-proposal <proposal.json> --idl <idl.json> [--corpus <dir>]\n"
    + "                                 [--out <report.md>] [--seed <int>] [--vectors <int>]\n"
    + "  fuaran-core-idl --help\n"
    + "\n"
    + "CLASSIFY\n"
    + "  Reads two committed artifact revisions and prints the advisory report — the\n"
    + "  wire severity of every changed member with the reason it applies, the host\n"
    + "  obligations it raises — then the verdict block: the class, the wire-profile\n"
    + "  evolution, whether emitters break, and the F# consequence classes a consumer\n"
    + "  compiled against the generated structural layer meets.\n"
    + "\n"
    + "  Advisory. Nothing is written, no version is bumped and no build is gated.\n"
    + "\n"
    + "  --manifest   the vocabulary's manifest, read ONLY for its host roster (`hosts`).\n"
    + "               Omitted, or without `hosts`, no host is obliged by name and the\n"
    + "               report says so.\n"
    + "  --expect     assert the verdict class. Exits 0 on a match, 1 on a mismatch.\n"
    + "  --support-before / --support-after\n"
    + "               each side's declared support (support.json), both or neither: a doc block,\n"
    + "               splice, case refine, kind projection or prelude that moved is then a\n"
    + "               host-surface row instead of reading as `unchanged`.\n"
    + "               One of: "
    + classes
    + "\n"
    + "\n"
    + "TABLE\n"
    + "  Prints the F# consequence table — the classes and the reason each applies.\n"
    + "  External surface guards and corpus gates cite this table rather than each\n"
    + "  re-deriving the mapping from the compiler's behaviour.\n"
    + "\n"
    + "SPIKE-PROPOSAL\n"
    + "  Prices a vocabulary-change proposal against the vocabulary in --idl without\n"
    + "  cutting a branch or writing a declaration: the delta is applied to an\n"
    + "  in-memory copy and four legs run (generate, corpus, fuzz, candidates), then the\n"
    + "  stability cost is reported. --corpus names the directory holding the `nodes/`\n"
    + "  family; without it the corpus leg reports not-checked and the run is not green.\n"
    + "  Exits 0 every leg passed, 1 a leg failed, 2 the document did not read. A green\n"
    + "  exit removes one objection; it is never a recommendation.\n"
    + "\n"
    + "EXIT CODES (classify, without --expect)\n"
    + "  0  unchanged / host-surface / additive — a consumer absorbs it by repinning\n"
    + "  3  breaking — the wire moved, or a conformant emitter stops conforming\n"
    + "  4  undecided — a change crosses an erased slot the artifact does not describe\n"
    + "  2  refused — bad usage, an unreadable file, or an artifact that did not parse\n"
    + "\n"
    + "  With --expect, the codes are 0 (match) / 1 (mismatch) / 2 (refused): an\n"
    + "  assertion has two outcomes, and conflating them with the branching codes\n"
    + "  above would make a passing assertion indistinguishable from a break.\n"

/// A refusal. Exit 2 throughout: the tool did not reach a verdict, which is a
/// different outcome from every verdict it can reach, and a gate must not read it
/// as one.
let private refuse (message: string) : int =
    printfn "fuaran-core-idl: %s" message
    2

let private readFile (label: string) (path: string) : Result<string, string> =
    try
        if File.Exists path then
            Ok(File.ReadAllText path)
        else
            Error(sprintf "%s not found: %s" label path)
    with e ->
        Error(sprintf "%s unreadable (%s): %s" label path e.Message)

/// Options after the two positional paths. Parsed by hand and STRICTLY: an
/// unrecognised flag is refused rather than ignored, because a gate that misspells
/// `--expect` must not get a pass from a tool that silently dropped its assertion.
type private Options =
    {
        Expect: string option
        Manifest: string option
        /// Phase 293 — each side's `support.json`, both or neither.
        SupportBefore: string option
        SupportAfter: string option
    }

let rec private options (acc: Options) (argv: string list) =
    match argv with
    | [] ->
        (match acc.SupportBefore, acc.SupportAfter with
         | Some _, None
         | None, Some _ ->
             Error
                 "--support-before and --support-after go together: a support document on one side only is a diff against nothing"
         | _ -> Ok acc)
    | "--expect" :: value :: rest -> options { acc with Expect = Some value } rest
    | "--manifest" :: value :: rest -> options { acc with Manifest = Some value } rest
    | "--support-before" :: value :: rest -> options { acc with SupportBefore = Some value } rest
    | "--support-after" :: value :: rest -> options { acc with SupportAfter = Some value } rest
    | [ "--expect" ] -> Error "--expect needs a class"
    | [ "--manifest" ] -> Error "--manifest needs a path"
    | [ "--support-before" ] -> Error "--support-before needs a path"
    | [ "--support-after" ] -> Error "--support-after needs a path"
    | other :: _ -> Error(sprintf "unrecognised option: %s" other)

let private classify (beforePath: string) (afterPath: string) (rest: string list) : int =
    match
        options
            { Expect = None
              Manifest = None
              SupportBefore = None
              SupportAfter = None }
            rest
    with
    | Error e -> refuse e
    | Ok opts ->
        let expect, manifestPath = opts.Expect, opts.Manifest

        let declared =
            match expect with
            | None -> Ok None
            | Some label ->
                match Diff.classOfLabel label with
                | Some c -> Ok(Some c)
                | None ->
                    Error(
                        sprintf
                            "unknown --expect class %s (one of: %s)"
                            label
                            (Diff.allClasses |> List.map Diff.classLabel |> String.concat " | ")
                    )

        let manifestText =
            match manifestPath with
            | None -> Ok None
            | Some p -> readFile "manifest" p |> Result.map Some

        let supportText (label: string) (path: string option) =
            match path with
            | None -> Ok None
            | Some p -> readFile label p |> Result.map Some

        let inputs =
            declared
            |> Result.bind (fun d -> manifestText |> Result.map (fun m -> d, m))
            |> Result.bind (fun (d, m) -> readFile "before" beforePath |> Result.map (fun b -> d, m, b))
            |> Result.bind (fun (d, m, b) -> readFile "after" afterPath |> Result.map (fun a -> d, m, b, a))
            |> Result.bind (fun (d, m, b, a) ->
                supportText "support-before" opts.SupportBefore
                |> Result.map (fun sb -> d, m, b, a, sb))
            |> Result.bind (fun (d, m, b, a, sb) ->
                supportText "support-after" opts.SupportAfter
                |> Result.map (fun sa -> d, m, b, a, sb, sa))

        match inputs with
        | Error e -> refuse e
        | Ok(declared, manifestText, beforeText, afterText, supportBefore, supportAfter) ->
            // Phase 292 — both artifacts are read as VOCABULARIES first, so a vocabulary that
            // is not well-formed is reported with every error `Declare.errors` names, before
            // any classification: a verdict about an artifact no loader would accept is a
            // verdict about nothing. The diff itself still reads the published JSON.
            let unreadable =
                [ "before", beforeText; "after", afterText ]
                |> List.choose (fun (side, text) ->
                    match Artifact.parse text with
                    | Ok _ -> None
                    | Error e -> Some(sprintf "%s: %s" side e))

            if not (List.isEmpty unreadable) then
                refuse (String.concat "\n" unreadable)
            else

                match Diff.runVerdictWith manifestText beforeText afterText supportBefore supportAfter with
                | Error e -> refuse e
                | Ok(text, verdict) ->
                    printf "%s" text
                    let actual = Diff.verdictClass verdict

                    match declared with
                    | None -> Diff.exitCode actual
                    | Some expected ->
                        if expected = actual then
                            printfn "expect: %s — MATCHED" (Diff.classLabel expected)
                            0
                        else
                            printfn
                                "expect: %s — but the artifacts classify as %s"
                                (Diff.classLabel expected)
                                (Diff.classLabel actual)

                            1

/// Phase 702 — price a vocabulary-change proposal against a vocabulary without cutting a
/// branch or writing a declaration. Phase 230 moved it here from the repository's own test
/// runner (where it was the `--spike-proposal` flag); the flags, the report and the exit
/// codes are unchanged, which is why its refusals still go to stderr rather than stdout.
///
/// The vocabulary is an ARGUMENT (`--idl <idl.json>`), read through `Artifact.parse` —
/// Phase 114's inversion is what makes that possible, and Phase 123 is where it was needed:
/// the entry point used to name a domain's vocabulary because that vocabulary happened to
/// live in the test project, which is exactly the coupling D14 removes. It is branchless by
/// construction — the delta is applied to an in-memory `Idl` value that exists for the
/// duration of the call — so an abandoned spike leaves no residue anywhere.
///
/// The corpus leg reads the `nodes/` family of the corpus directory named by `--corpus
/// <dir>`. When none is named the leg reports "not checked" and the run is not green: a
/// spike whose additive claim went unexamined must not read as a spike that examined it and
/// found nothing.
///
/// Exit: 0 every leg passed · 1 a leg failed · 2 the document did not read. A green exit is
/// the removal of one objection, never a recommendation — nothing downstream of this command
/// may treat 0 as an admission.
let private spikeProposal (proposalPath: string) (rest: string list) : int =
    let flag name =
        rest
        |> List.pairwise
        |> List.tryPick (fun (a, b) -> if a = name then Some b else None)

    let intFlag name fallback =
        match flag name with
        | Some v ->
            match System.Int32.TryParse v with
            | true, n -> n
            | _ -> fallback
        | None -> fallback

    let corpus =
        match flag "--corpus" with
        | None -> []
        | Some root ->
            let dir = Path.Combine(root, "nodes")

            if not (Directory.Exists dir) then
                []
            else
                Directory.GetFiles(dir, "*.json")
                |> Array.filter (fun p -> not ((Path.GetFileName p).EndsWith ".expected.json"))
                |> Array.sortWith (fun a b -> System.String.CompareOrdinal(a, b))
                |> Array.map (fun p -> Path.GetFileName p, File.ReadAllText p)
                |> List.ofArray

    let baseIdl =
        match flag "--idl" with
        | None -> Error "no --idl <idl.json> given — the spike prices a proposal AGAINST a vocabulary"
        | Some path ->
            if File.Exists path then
                Artifact.parse (File.ReadAllText path)
            else
                Error(sprintf "--idl names no file: %s" path)

    match baseIdl, Proposal.parse (File.ReadAllText proposalPath) with
    | Error e, _ ->
        eprintfn "spike-proposal: the vocabulary did not read — %s" e
        2
    | _, Error e ->
        eprintfn "spike-proposal: the document did not read — %s" e
        2
    | Ok baseVocabulary, Ok proposal ->
        match
            ProposalSpike.run
                { Base = baseVocabulary
                  Proposal = proposal
                  Corpus = corpus
                  // Pinned, not clock-derived: a divergence a spike finds has to
                  // reproduce from the report alone on another machine.
                  FuzzSeed = intFlag "--seed" 20260826
                  FuzzVectors = intFlag "--vectors" 200
                  External = [] }
        with
        | Error e ->
            eprintfn "spike-proposal: %s" e
            2
        | Ok report ->
            let text = ProposalSpike.render report

            match flag "--out" with
            | Some out ->
                File.WriteAllText(out, text)
                printfn "wrote %s" out
            | None -> printf "%s" text

            if report.Green then 0 else 1

/// Dispatches on the verb. Exit 2 means no verdict was reached (bad usage, an unknown verb,
/// an empty argument list, an unreadable or malformed input); `--help` exits 0. Every other
/// code is the verb's own — see the usage text.
[<EntryPoint>]
let main argv =
    match List.ofArray argv with
    | "classify" :: before :: after :: rest -> classify before after rest
    | [ "classify" ]
    | [ "classify"; _ ] -> refuse "classify needs two paths: <before.json> <after.json>"
    | "spike-proposal" :: proposalPath :: rest -> spikeProposal proposalPath rest
    | [ "spike-proposal" ] -> refuse "spike-proposal needs a proposal document: <proposal.json>"
    | [ "table" ] ->
        printf "%s" Diff.consequenceTable
        0
    | [ "--help" ]
    | [ "-h" ]
    | [ "help" ] ->
        printf "%s" usage
        0
    | [] ->
        printf "%s" usage
        2
    | verb :: _ ->
        printf "%s" usage
        refuse (sprintf "unknown verb: %s" verb)
