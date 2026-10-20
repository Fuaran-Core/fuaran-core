/// The `fuaran-core-idl` command: the IDL stability classifier (`classify`), the F#
/// consequence table (`table`) and the proposal pricer (`spike-proposal`) over the
/// `Fuaran.Core.Idl.Diff` library, for repositories with no F# build of their own.
module Fuaran.Core.Idl.Cli.Program

open System
open System.IO
open System.Text
open Fuaran.Core.Idl
open Fuaran.Core.Idl.Codegen.Types

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
// Phase 526 — ONE verb table. Each verb declares its positionals and its flags as data, ONE
// parser reads every verb's arguments from that declaration, the usage text is rendered from
// the same table, and every outcome reaches the process through ONE map ([[exitCode]]). Before
// it the tool carried two hand-rolled parsers and two refusal channels: `spike-proposal` (moved
// in from the test runner at Phase 230) refused on stderr while `classify` refused on stdout.
// The tool now has one refusal channel and it is stdout, `classify`'s: everything the tool
// writes, refusals included, is one stream, so a gate that captures the output gets the whole
// record and nothing depends on how the calling shell treats a native command's stderr.
//
// FSharp.Core only — no argument-parsing dependency. The surface is three verbs and
// a few options; a parser combinator library for that would be a dependency every
// consumer of the tool inherits in exchange for nothing.
// ---------------------------------------------------------------------------

/// What a verb's run comes to. [[exitCode]] is the one map from it to the process's exit code.
type private Outcome =
    /// A verdict reached, for a caller to branch on: 0 / 3 / 4 (`Diff.exitCode`).
    | Classified of VerdictClass
    /// An asserted class (`--expect`): 0 when it matched, 1 when it did not. An assertion has two
    /// outcomes, and sharing the branching codes would make a passing assertion read as a break.
    | Asserted of matched: bool
    /// The spike's legs: 0 when every leg passed, 1 when one failed.
    | Spiked of green: bool
    /// What was asked for was printed (the table, the usage): 0.
    | Printed
    /// No verdict was reached — bad usage, an input that did not read or parse, an output that
    /// could not be written: 2, which is none of the verdicts, so a gate cannot read it as one.
    | Refused of string

let private exitCode (outcome: Outcome) : int =
    match outcome with
    | Classified c -> Diff.exitCode c
    | Asserted true
    | Spiked true
    | Printed -> 0
    | Asserted false
    | Spiked false -> 1
    | Refused _ -> 2

/// One flag a verb takes, always with a value.
type private Flag =
    {
        Name: string
        /// The value's metavariable in the synopsis (`<class>`).
        Value: string
        /// The phrase the refusal of a valueless flag ends with (`--expect needs a class`).
        Needs: string
        /// The value must be an integer; the parser refuses one that is not.
        Integer: bool
        /// `Some why` when the verb cannot run without it: the parser refuses its absence with
        /// `why`, and the synopsis prints it without brackets.
        Required: string option
        /// The flag's lines in the usage text.
        Help: string list
    }

/// The arguments one verb was given, read against its declaration by [[parse]].
type private Args =
    { Positionals: string list
      Flags: Map<string, string> }

    member a.Flag(name: string) : string option = Map.tryFind name a.Flags

    /// A `Required` flag's value — present, because [[parse]] refused its absence.
    member a.Required(name: string) : string = Map.find name a.Flags

/// A verb: what it takes, what the usage says about it, and what it does.
type private Verb =
    {
        Name: string
        /// The positionals' metavariables, all required, before any flag.
        Positionals: string list
        /// What the arity refusal says the verb needs (`two paths`).
        Needs: string
        Flags: Flag list
        /// The verb's section of the usage text.
        About: string list
        Run: Args -> Outcome
    }

let private flag (name: string) (value: string) (needs: string) (help: string list) : Flag =
    { Name = name
      Value = value
      Needs = needs
      Integer = false
      Required = None
      Help = help }

/// An integer flag's value, read one way wherever it is read: decimal digits with an optional
/// sign, culture-invariant.
let private integer (value: string) : int option =
    match
        Int32.TryParse(value, Globalization.NumberStyles.AllowLeadingSign, Globalization.CultureInfo.InvariantCulture)
    with
    | true, n -> Some n
    | _ -> None

/// THE argument parser, for every verb. STRICT: the positionals and the `Required` flags must be
/// there, and an unrecognised flag, a flag without its value, a flag given twice and a
/// non-integer value for an integer flag are each refused rather than ignored, dropped or defaulted — a gate that misspells
/// `--expect` must not get a pass from a tool that silently dropped its assertion, and a spike run
/// on a seed the operator did not ask for reports on vectors nobody chose.
let private parse (verb: Verb) (argv: string list) : Result<Args, string> =
    let arity = List.length verb.Positionals

    let rec flags (acc: Map<string, string>) (rest: string list) =
        match rest with
        | [] ->
            match
                verb.Flags
                |> List.tryPick (fun f ->
                    if acc.ContainsKey f.Name then
                        None
                    else
                        f.Required |> Option.map (fun why -> f, why))
            with
            | Some(f, why) -> Error(sprintf "no %s <%s> given — %s" f.Name f.Value why)
            | None ->
                Ok
                    { Positionals = List.truncate arity argv
                      Flags = acc }
        | name :: tail ->
            match verb.Flags |> List.tryFind (fun f -> f.Name = name), tail with
            | None, _ -> Error(sprintf "unrecognised option: %s" name)
            | Some f, [] -> Error(sprintf "%s needs %s" name f.Needs)
            | Some _, _ when acc.ContainsKey name -> Error(sprintf "%s given twice" name)
            | Some f, value :: _ when f.Integer && Option.isNone (integer value) ->
                Error(sprintf "%s takes an integer, not '%s'" name value)
            | Some _, value :: tail -> flags (Map.add name value acc) tail

    if List.length argv < arity then
        Error(
            sprintf
                "%s needs %s: %s"
                verb.Name
                verb.Needs
                (verb.Positionals |> List.map (fun p -> "<" + p + ">") |> String.concat " ")
        )
    else
        flags Map.empty (List.skip arity argv)

let private readFile (label: string) (path: string) : Result<string, string> =
    try
        if File.Exists path then
            Ok(File.ReadAllText path)
        else
            Error(sprintf "%s not found: %s" label path)
    with e ->
        Error(sprintf "%s unreadable (%s): %s" label path e.Message)

/// A file named by an optional flag: absent is `None`, present-and-unreadable is the refusal.
let private readOptional (label: string) (path: string option) : Result<string option, string> =
    match path with
    | None -> Ok None
    | Some p -> readFile label p |> Result.map Some

let private classLabels () =
    Diff.allClasses |> List.map Diff.classLabel |> String.concat " | "

// ---- classify ---------------------------------------------------------------

let private classify (args: Args) : Outcome =
    let beforePath, afterPath = args.Positionals[0], args.Positionals[1]

    let declared =
        match args.Flag "--expect" with
        | None -> Ok None
        | Some label ->
            match Diff.classOfLabel label with
            | Some c -> Ok(Some c)
            | None -> Error(sprintf "unknown --expect class %s (one of: %s)" label (classLabels ()))

    let paired =
        match args.Flag "--support-before", args.Flag "--support-after" with
        | Some _, None
        | None, Some _ ->
            Error
                "--support-before and --support-after go together: a support document on one side only is a diff against nothing"
        | _ -> Ok()

    let inputs =
        paired
        |> Result.bind (fun () -> declared)
        |> Result.bind (fun d -> readOptional "manifest" (args.Flag "--manifest") |> Result.map (fun m -> d, m))
        |> Result.bind (fun (d, m) -> readFile "before" beforePath |> Result.map (fun b -> d, m, b))
        |> Result.bind (fun (d, m, b) -> readFile "after" afterPath |> Result.map (fun a -> d, m, b, a))
        |> Result.bind (fun (d, m, b, a) ->
            readOptional "support-before" (args.Flag "--support-before")
            |> Result.map (fun sb -> d, m, b, a, sb))
        |> Result.bind (fun (d, m, b, a, sb) ->
            readOptional "support-after" (args.Flag "--support-after")
            |> Result.map (fun sa -> d, m, b, a, sb, sa))

    match inputs with
    | Error e -> Refused e
    | Ok(declared, manifestText, beforeText, afterText, supportBefore, supportAfter) ->
        // Phase 292 — both artifacts are read as VOCABULARIES first, so a vocabulary that is not
        // well-formed is reported with every error `Declare.errors` names, before any
        // classification: a verdict about an artifact no loader would accept is a verdict about
        // nothing. The diff itself reads the published JSON as a snapshot, once per side.
        let unreadable =
            [ "before", beforeText; "after", afterText ]
            |> List.choose (fun (side, text) ->
                match Artifact.parse text with
                | Ok _ -> None
                | Error e -> Some(sprintf "%s: %s" side e))

        if not (List.isEmpty unreadable) then
            Refused(String.concat "\n" unreadable)
        else
            match Diff.run manifestText (beforeText, supportBefore) (afterText, supportAfter) with
            | Error e -> Refused e
            | Ok(text, verdict) ->
                printf "%s" text
                let actual = Diff.verdictClass verdict

                match declared with
                | None -> Classified actual
                | Some expected when expected = actual ->
                    printfn "expect: %s — MATCHED" (Diff.classLabel expected)
                    Asserted true
                | Some expected ->
                    printfn
                        "expect: %s — but the artifacts classify as %s"
                        (Diff.classLabel expected)
                        (Diff.classLabel actual)

                    Asserted false

// ---- spike-proposal -----------------------------------------------------------

/// The corpus leg's `nodes/` documents under `root`, ordinally sorted, each read through
/// `readFile`. A `--corpus` that names no directory is refused, as an `--idl` naming no file is;
/// a root without a `nodes/` family is an empty corpus, which the leg reports as not checked.
let private readCorpus (root: string) : Result<(string * string) list, string> =
    if not (Directory.Exists root) then
        Error(sprintf "--corpus names no directory: %s" root)
    else
        let dir = Path.Combine(root, "nodes")

        if not (Directory.Exists dir) then
            Ok []
        else
            let listed =
                try
                    Ok(Directory.GetFiles(dir, "*.json"))
                with e ->
                    Error(sprintf "corpus unreadable (%s): %s" dir e.Message)

            listed
            |> Result.bind (fun files ->
                files
                |> Array.filter (fun p -> not ((Path.GetFileName p).EndsWith ".expected.json"))
                |> Array.sortWith (fun a b -> String.CompareOrdinal(a, b))
                |> Array.toList
                |> List.fold
                    (fun acc p ->
                        acc
                        |> Result.bind (fun docs ->
                            readFile "corpus document" p
                            |> Result.map (fun text -> (Path.GetFileName p, text) :: docs)))
                    (Ok [])
                |> Result.map List.rev)

/// The spike's pinned defaults — not clock-derived: a divergence a spike finds has to reproduce
/// from the report alone on another machine.
let private defaultSeed = 20260826

let private defaultVectors = 200

/// Phase 702 — price a vocabulary-change proposal against a vocabulary without cutting a
/// branch or writing a declaration. Phase 230 moved it here from the repository's own test
/// runner (where it was the `--spike-proposal` flag).
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
/// Every read and the `--out` write are guarded (Phase 384): a missing proposal or vocabulary
/// file, a `--corpus` naming no directory and an unwritable `--out` are each a one-line refusal,
/// never a stack trace. A green exit is the removal of one objection, never a recommendation —
/// nothing downstream of this command may treat 0 as an admission.
let private spikeProposal (args: Args) : Outcome =
    let integerFlag name fallback =
        args.Flag name |> Option.bind integer |> Option.defaultValue fallback

    let idl = args.Required "--idl"

    let inputs =
        (if File.Exists idl then
             readFile "--idl" idl
         else
             Error(sprintf "--idl names no file: %s" idl))
        |> Result.bind (fun text ->
            Artifact.parse text
            |> Result.mapError (fun e -> "the vocabulary did not read — " + e))
        |> Result.bind (fun vocabulary ->
            readFile "proposal" args.Positionals[0]
            |> Result.bind (fun text ->
                Proposal.parse text
                |> Result.mapError (fun e -> "the document did not read — " + e))
            |> Result.map (fun proposal -> vocabulary, proposal))
        |> Result.bind (fun (vocabulary, proposal) ->
            match args.Flag "--corpus" with
            | None -> Ok(vocabulary, proposal, [])
            | Some root -> readCorpus root |> Result.map (fun corpus -> vocabulary, proposal, corpus))

    match inputs with
    | Error e -> Refused e
    | Ok(baseVocabulary, proposal, corpus) ->
        let report =
            ProposalSpike.run
                { Base = baseVocabulary
                  Proposal = proposal
                  Corpus = corpus
                  FuzzSeed = integerFlag "--seed" defaultSeed
                  FuzzVectors = integerFlag "--vectors" defaultVectors }

        let text = ProposalSpike.render report

        match args.Flag "--out" with
        | None ->
            printf "%s" text
            Spiked report.Green
        | Some out ->
            let written =
                try
                    File.WriteAllText(out, text)
                    Ok()
                with e ->
                    Error(sprintf "--out unwritable (%s): %s" out e.Message)

            match written with
            | Error e -> Refused e
            | Ok() ->
                printfn "wrote %s" out
                Spiked report.Green

// ---- the verb table -----------------------------------------------------------

let private verbs: Verb list =
    [ { Name = "classify"
        Positionals = [ "before.json"; "after.json" ]
        Needs = "two paths"
        Flags =
          [ flag
                "--manifest"
                "manifest.json"
                "a path"
                [ "the vocabulary's manifest, read ONLY for its host roster (`hosts`)."
                  "Omitted, or without `hosts`, no host is obliged by name and the"
                  "report says so." ]
            flag
                "--expect"
                "class"
                "a class"
                [ "assert the verdict class. Exits 0 on a match, 1 on a mismatch."
                  "One of: " + classLabels () ]
            flag
                "--support-before"
                "support.json"
                "a path"
                [ "each side's declared support (support.json), both or neither: a doc"
                  "block, splice, case refine, kind projection or prelude that moved is"
                  "then a host-surface row instead of reading as `unchanged`." ]
            flag "--support-after" "support.json" "a path" [ "the other side's, with --support-before." ] ]
        About =
          [ "Reads two committed artifact revisions and prints the advisory report — the"
            "wire severity of every changed member with the reason it applies, the host"
            "obligations it raises — then the verdict block: the class, the wire-profile"
            "evolution, whether emitters break, and the F# consequence classes a consumer"
            "compiled against the generated structural layer meets."
            ""
            "Advisory. Nothing is written, no version is bumped and no build is gated."
            ""
            "Exits 0 unchanged / host-surface / additive — a consumer absorbs it by"
            "repinning; 3 breaking — the wire moved, or a conformant emitter stops"
            "conforming; 4 undecided — a change crosses an erased slot the artifact does"
            "not describe. With --expect: 0 match, 1 mismatch — an assertion has two"
            "outcomes, and sharing the branching codes would make a passing assertion"
            "indistinguishable from a break." ]
        Run = classify }
      { Name = "table"
        Positionals = []
        Needs = "nothing"
        Flags = []
        About =
          [ "Prints the F# consequence table — the classes and the reason each applies."
            "External surface guards and corpus gates cite this table rather than each"
            "re-deriving the mapping from the compiler's behaviour. Exits 0." ]
        Run =
          fun _ ->
              printf "%s" Diff.consequenceTable
              Printed }
      { Name = "spike-proposal"
        Positionals = [ "proposal.json" ]
        Needs = "a proposal document"
        Flags =
          [ { flag "--idl" "idl.json" "a path" [ "the vocabulary the proposal is priced against." ] with
                Required = Some "the spike prices a proposal AGAINST a vocabulary" }
            flag
                "--corpus"
                "dir"
                "a path"
                [ "the directory holding the `nodes/` family; without it the corpus leg"
                  "reports not-checked and the run is not green." ]
            flag "--out" "report.md" "a path" [ "write the report there instead of printing it." ]
            { flag "--seed" "int" "an integer" [ sprintf "the fuzz leg's seed (default %d)." defaultSeed ] with
                Integer = true }
            { flag "--vectors" "int" "an integer" [ sprintf "the fuzz leg's vector count (default %d)." defaultVectors ] with
                Integer = true } ]
        About =
          [ "Prices a vocabulary-change proposal against the vocabulary in --idl without"
            "cutting a branch or writing a declaration: the delta is applied to an"
            "in-memory copy and four legs run — generate (every backend), corpus, fuzz"
            "and candidates — then the stability cost is reported."
            ""
            "Exits 0 every leg passed, 1 a leg failed. A green exit removes one"
            "objection; it is never a recommendation." ]
        Run = spikeProposal } ]

/// The usage text, rendered from [[verbs]]: a synopsis per verb, then each verb's section with
/// its flags, then the one refusal contract every verb shares.
let private usage: string =
    let sb = StringBuilder()
    let line (s: string) = sb.Append(s).Append('\n') |> ignore
    let width = 96

    let synopsis (v: Verb) =
        let head =
            "  fuaran-core-idl "
            + String.concat " " (v.Name :: (v.Positionals |> List.map (fun p -> "<" + p + ">")))

        let indent = String(' ', 2 + "fuaran-core-idl ".Length + v.Name.Length + 1)

        let token (f: Flag) =
            let bare = sprintf "%s <%s>" f.Name f.Value
            if Option.isSome f.Required then bare else "[" + bare + "]"

        (head, v.Flags |> List.map token)
        ||> List.fold (fun current token ->
            let last = current.Substring(current.LastIndexOf '\n' + 1)

            if last.Length + 1 + token.Length <= width then
                current + " " + token
            else
                current + "\n" + indent + token)

    line "fuaran-core-idl — the IDL stability classifier over two `idl.json` revisions."
    line ""
    line "USAGE"

    for v in verbs do
        line (synopsis v)

    line "  fuaran-core-idl --help"

    for v in verbs do
        line ""
        line (v.Name.ToUpperInvariant())

        for about in v.About do
            line (if about = "" then "" else "  " + about)

        let pad = v.Flags |> List.map (fun f -> f.Name.Length) |> List.fold max 0 |> (+) 2

        if not (List.isEmpty v.Flags) then
            line ""

        for f in v.Flags do
            f.Help
            |> List.iteri (fun i h -> line ("  " + (if i = 0 then f.Name.PadRight pad else String(' ', pad)) + " " + h))

    line ""
    line "REFUSALS"
    line "  Every verb refuses the same way: one line on stdout, `fuaran-core-idl: <sentence>`,"
    line "  and exit 2 — bad usage (an unknown verb or flag, a missing argument or value, a flag"
    line "  given twice, a non-integer --seed or --vectors), an input that did not read or parse,"
    line "  or an output that could not be written. 2 is none of the verdict codes: the tool"
    line "  reached no verdict, and a gate must not read it as one."
    sb.ToString()

/// The one refusal channel: stdout, prefixed with the command's name.
let private refuse (message: string) : int =
    printfn "fuaran-core-idl: %s" message
    exitCode (Refused message)

/// Dispatches on the verb through [[verbs]]. `--help` exits 0; no arguments print the usage and
/// exit 2, and an unknown verb prints it and refuses — neither reached a verdict.
[<EntryPoint>]
let main argv =
    // The usage text and reports carry non-ASCII characters (the em dash). Left ambient, a redirected
    // stream is encoded with the console's code page, so the bytes would depend on the machine that
    // ran the command. Pin both streams to UTF-8 without a byte-order mark, before any write.
    // Setting `Console.OutputEncoding` replaces the writers behind both `Console.Out` and
    // `Console.Error`; the error writer is then set explicitly so the pairing does not rest on that.
    let utf8 = UTF8Encoding false
    Console.OutputEncoding <- utf8

    let err = new StreamWriter(Console.OpenStandardError(), utf8)
    err.AutoFlush <- true
    Console.SetError err

    match List.ofArray argv with
    | [ "--help" ]
    | [ "-h" ]
    | [ "help" ] ->
        printf "%s" usage
        exitCode Printed
    | [] ->
        printf "%s" usage
        exitCode (Refused "no verb")
    | name :: rest ->
        match verbs |> List.tryFind (fun v -> v.Name = name) with
        | None ->
            printf "%s" usage
            refuse (sprintf "unknown verb: %s" name)
        | Some verb ->
            match parse verb rest |> Result.map verb.Run with
            | Error e
            | Ok(Refused e) -> refuse e
            | Ok outcome -> exitCode outcome
