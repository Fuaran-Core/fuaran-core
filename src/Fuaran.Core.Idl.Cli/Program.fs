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
// Everything is written to stdout, including refusals. A gate that captures the
// command's output gets the whole record in one stream, and nothing depends on
// how the calling shell treats a native command's stderr.
//
// FSharp.Core only — no argument-parsing dependency. The surface is two verbs and
// two options; a parser combinator library for that would be a dependency every
// consumer of the tool inherits in exchange for nothing.
// ---------------------------------------------------------------------------

let private usage =
    let classes = Diff.allClasses |> List.map Diff.classLabel |> String.concat " | "

    "fuaran-core-idl — the IDL stability classifier over two `idl.json` revisions.\n"
    + "\n"
    + "USAGE\n"
    + "  fuaran-core-idl classify <before.json> <after.json> [--manifest <manifest.json>]\n"
    + "                                                      [--expect <class>]\n"
    + "  fuaran-core-idl table\n"
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
    + "  --manifest   the conformance corpus manifest, read ONLY for its host roster.\n"
    + "               Omitted, the declared roster is used and the report says so.\n"
    + "  --expect     assert the verdict class. Exits 0 on a match, 1 on a mismatch.\n"
    + "               One of: "
    + classes
    + "\n"
    + "\n"
    + "TABLE\n"
    + "  Prints the F# consequence table — the classes and the reason each applies.\n"
    + "  External surface guards and corpus gates cite this table rather than each\n"
    + "  re-deriving the mapping from the compiler's behaviour.\n"
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
let rec private options (expect: string option) (manifest: string option) (argv: string list) =
    match argv with
    | [] -> Ok(expect, manifest)
    | "--expect" :: value :: rest -> options (Some value) manifest rest
    | "--manifest" :: value :: rest -> options expect (Some value) rest
    | [ "--expect" ] -> Error "--expect needs a class"
    | [ "--manifest" ] -> Error "--manifest needs a path"
    | other :: _ -> Error(sprintf "unrecognised option: %s" other)

let private classify (beforePath: string) (afterPath: string) (rest: string list) : int =
    match options None None rest with
    | Error e -> refuse e
    | Ok(expect, manifestPath) ->

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

        let inputs =
            declared
            |> Result.bind (fun d -> manifestText |> Result.map (fun m -> d, m))
            |> Result.bind (fun (d, m) -> readFile "before" beforePath |> Result.map (fun b -> d, m, b))
            |> Result.bind (fun (d, m, b) -> readFile "after" afterPath |> Result.map (fun a -> d, m, b, a))

        match inputs with
        | Error e -> refuse e
        | Ok(declared, manifestText, beforeText, afterText) ->
            match Diff.runVerdict manifestText beforeText afterText with
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

[<EntryPoint>]
let main argv =
    match List.ofArray argv with
    | "classify" :: before :: after :: rest -> classify before after rest
    | [ "classify" ]
    | [ "classify"; _ ] -> refuse "classify needs two paths: <before.json> <after.json>"
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
