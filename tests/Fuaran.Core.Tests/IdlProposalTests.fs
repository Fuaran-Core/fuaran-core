module Fuaran.Core.Tests.IdlProposalTests

open Expecto
open Fuaran.Core
open Fuaran.Core.Idl
open Fuaran.Core.Idl.Cli

// ---------------------------------------------------------------------------
// The proposal document and the branchless spike.
//
// Every case here runs against the eight-kind `miniIdl` rather than the real
// vocabulary, deliberately: the engine is domain-generic, and a test that pinned
// a real kind's field set would fail the next time that vocabulary grew — for a
// reason having nothing to do with what it claims to check.
//
// The candidate fixtures are BUILT by encoding a value through the post-delta
// vocabulary rather than hand-authored as JSON text. Hand-authoring would make
// every assertion depend on getting canonical key order and escaping right by
// hand, which is a second implementation of the encoder hiding inside a test.
// ---------------------------------------------------------------------------

module Mini = Fuaran.Core.Tests.MiniIdl

/// A complete, admissible proposal document with one field addition. Every test
/// that wants a DEFECT starts from this and removes exactly one thing, so the
/// defect it asserts is the only difference from a clean document.
let private completeJson (extraDeltaJson: string) (fixtureWire: string) =
    sprintf
        """
{
  "proposalVersion": 1,
  "id": "badge-tooltip",
  "cluster": "hover-hint sightings",
  "draftedBy": "test",
  "draftedAt": "2026-08-21T00:00:00Z",
  "delta": [ %s ],
  "candidateFixtures": [ { "name": "badge-with-tooltip", "wire": %s } ],
  "evidence": [
    { "signal": "emission-miss", "runId": "run-0001", "promptDigest": "sha256:abcd", "count": 7,
      "detail": "seven emissions carried a hover hint the vocabulary drops" }
  ],
  "irreducibility": "a hint is not a child node and has no composition that survives decode",
  "alternatives": [
    { "disposition": "normalisation", "verdict": "insufficient", "argument": "there is no near-canonical spelling to normalise TO" },
    { "disposition": "teaching", "verdict": "insufficient", "argument": "the emissions are correct in intent; nothing to teach" },
    { "disposition": "variant", "verdict": "insufficient", "argument": "no existing spec union owns hover text" }
  ],
  "normalisationDistinction": "no spelling was ever retired here, so nothing would be re-admitted",
  "confusionPlan": "teach the field in a branch pack and re-run the affected slice at n=9 per posture"
}
"""
        extraDeltaJson
        fixtureWire

let private tooltipDelta =
    """{ "op": "addField",
         "owner": { "$type": "kind", "name": "Badge" },
         "field": { "name": "tooltip", "type": { "$type": "str" }, "optionality": { "$type": "optional" } } }"""

/// The post-delta vocabulary a fixture is built against — never persisted, which
/// is the module's first invariant exercised as a fact rather than asserted as a
/// comment.
let private postIdl =
    match
        Proposal.applyDelta
            Mini.miniIdl
            [ AddField(
                  OwnerKind "Badge",
                  { Name = "tooltip"
                    Type = TStr
                    Opt = Optional
                    Aliases = []
                    Presence = None
                    Annotations = Annotations.Empty }
              ) ]
    with
    | Ok idl -> idl
    | Error e -> failwithf "fixture setup: %s" e

let private badgeValue (withTooltip: bool) =
    VNode(
        "badge-1",
        "Badge",
        [ "label", VUnion("Literal", [ "text", VStr "Beta" ])
          "variant", VEnum "Info"
          if withTooltip then
              "tooltip", VStr "a hover hint" ]
    )

let private encodeWith idl v =
    match Encode.encode idl v with
    | Ok w -> w
    | Error e -> failwithf "fixture setup encode: %s" e

let private candidateWire = encodeWith postIdl (badgeValue true)
let private plainBadgeWire = encodeWith Mini.miniIdl (badgeValue false)

let private parseOrFail (text: string) =
    match Proposal.parse text with
    | Ok p -> p
    | Error e -> failtestf "proposal did not parse: %s" e

let private spike (p: Proposal) =
    ProposalSpike.run
        { Base = Mini.miniIdl
          Proposal = p
          Corpus = [ "plain-badge", plainBadgeWire ]
          FuzzSeed = 20260821
          FuzzVectors = 200 }

let private legOf (r: SpikeReport) (name: string) =
    match r.Legs |> List.tryFind (fun l -> l.Name = name) with
    | Some l -> l
    | None -> failtestf "no '%s' leg in the report (legs: %A)" name (r.Legs |> List.map _.Name)

/// Phase 230 — the operator command the harness now lives behind. The CLI assembly is the one this
/// suite compiled against, so its own copy in the test output is the one run; it is shelled rather
/// than called, because an exit code returned by a function is not evidence about a process.
let private cliDll = typeof<SpikeInput>.Assembly.Location

/// Run `fuaran-core-idl <args>`: exit code, stdout, stderr, through `ChildProcess.run`, which drains
/// both pipes at once so a chatty child cannot fill one while the other is being read.
let private runCli (args: string) : int * string * string =
    let p =
        ChildProcess.run (ChildProcess.command "dotnet" ("\"" + cliDll + "\" " + args))
        |> Result.defaultWith failwith

    p.Exit, p.Stdout, p.Stderr

/// A scratch directory holding the three inputs the command reads: the vocabulary artifact, the
/// proposal, and a corpus directory with one `nodes/` document.
let private withCommandInputs (proposalJson: string) (check: (string -> string) -> unit) : unit =
    let root =
        System.IO.Path.Combine(System.AppContext.BaseDirectory, "spike-proposal-" + System.Guid.NewGuid().ToString("N"))

    try
        let nodes = System.IO.Path.Combine(root, "corpus", "nodes")
        System.IO.Directory.CreateDirectory nodes |> ignore
        System.IO.File.WriteAllText(System.IO.Path.Combine(nodes, "plain-badge.json"), plainBadgeWire)
        System.IO.File.WriteAllText(System.IO.Path.Combine(root, "idl.json"), Artifact.render Mini.miniIdl)
        System.IO.File.WriteAllText(System.IO.Path.Combine(root, "proposal.json"), proposalJson)
        check (fun name -> System.IO.Path.Combine(root, name))
    finally
        if System.IO.Directory.Exists root then
            System.IO.Directory.Delete(root, true)

/// The command's own defaults for the two generative flags, so the in-process run below is the
/// run the command makes when neither is passed.
let private commandSeed = 20260826
let private commandVectors = 200

[<Tests>]
let tests =
    testList
        "IDL vocabulary proposals"
        [ testList
              "the document"
              [ testCase "a complete proposal reads and validates clean" (fun _ ->
                    let p = parseOrFail (completeJson tooltipDelta candidateWire)
                    Expect.equal p.Id "badge-tooltip" "id"
                    Expect.equal (List.length p.Delta) 1 "one delta op"
                    Expect.isEmpty (Proposal.validate p) "a complete document has no defects")

                testCase "a missing alternative disposition is named, not tolerated" (fun _ ->
                    // The mandatory-alternatives rule is the one a drafter is most
                    // likely to skip and the one whose absence is least visible in
                    // a well-written proposal, so it is checked per disposition.
                    for dropped in Proposal.requiredAlternatives do
                        let text =
                            (completeJson tooltipDelta candidateWire)
                                .Replace(sprintf "\"disposition\": \"%s\"" dropped, "\"disposition\": \"unrelated\"")

                        let defects = Proposal.validate (parseOrFail text)

                        Expect.isTrue
                            (defects |> List.exists (fun d -> d.Contains dropped))
                            (sprintf "dropping the '%s' alternative must be reported" dropped))

                testCase "an evidence citation with no run reference is a defect" (fun _ ->
                    let text =
                        (completeJson tooltipDelta candidateWire).Replace("\"runId\": \"run-0001\"", "\"runId\": \"\"")

                    let defects = Proposal.validate (parseOrFail text)

                    Expect.isTrue
                        (defects |> List.exists (fun d -> d.Contains "runId"))
                        "absence of a reference must not pass as a reference")

                testCase "an evidence citation with no prompt digest is a defect" (fun _ ->
                    let text =
                        (completeJson tooltipDelta candidateWire)
                            .Replace("\"promptDigest\": \"sha256:abcd\"", "\"promptDigest\": \"\"")

                    Expect.isTrue
                        (Proposal.validate (parseOrFail text)
                         |> List.exists (fun d -> d.Contains "promptDigest"))
                        "a sighting that cannot be re-read against its prompt is not a citation")

                testCase "a priced normalisation with no re-admission distinction is a defect" (fun _ ->
                    let text =
                        (completeJson tooltipDelta candidateWire)
                            .Replace(
                                "\"normalisationDistinction\": \"no spelling was ever retired here, so nothing would be re-admitted\"",
                                "\"normalisationDistinction\": \"\""
                            )

                    Expect.isTrue
                        (Proposal.validate (parseOrFail text)
                         |> List.exists (fun d -> d.Contains "normalisationDistinction"))
                        "re-admitting and admitting must be told apart explicitly")

                testCase "a host-surface type cannot be minted by a proposal" (fun _ ->
                    let hostDelta =
                        """{ "op": "addField",
                             "owner": { "$type": "kind", "name": "Badge" },
                             "field": { "name": "onHover",
                                        "type": { "$type": "closure", "wire": "<closure>" },
                                        "optionality": { "$type": "optional" } } }"""

                    match Proposal.parseDetailed (completeJson hostDelta candidateWire) with
                    | Ok _ -> failtest "a closure slot was accepted from a proposal document"
                    | Error e ->
                        // Phase 520: the code, the path and the projected sentence, not a phrase of it
                        Expect.equal
                            (e.Code, e.Path)
                            (DecodeCode.NotAdmitted,
                             [ PathSegment.Key "delta"
                               PathSegment.Index 0
                               PathSegment.Key "field"
                               PathSegment.Key "type"
                               PathSegment.Key "$type" ])
                            "NotAdmitted at the type's tag"

                        Expect.equal
                            e.Message
                            (DecodeError.sentence e.Code "a wire-data type" "the host-surface type 'closure'")
                            "the refusal names why")

                testCase "a host-only optionality cannot be minted by a proposal" (fun _ ->
                    let hostOnly =
                        tooltipDelta.Replace("{ \"$type\": \"optional\" }", "{ \"$type\": \"hostOnly\" }")

                    match Proposal.parseDetailed (completeJson hostOnly candidateWire) with
                    | Ok _ -> failtest "a wire-invisible slot was accepted from a proposal document"
                    | Error e ->
                        Expect.equal
                            (e.Code, e.Path)
                            (DecodeCode.NotAdmitted,
                             [ PathSegment.Key "delta"
                               PathSegment.Index 0
                               PathSegment.Key "field"
                               PathSegment.Key "optionality"
                               PathSegment.Key "$type" ])
                            "NotAdmitted at the optionality's tag"

                        Expect.equal
                            e.Message
                            (DecodeError.sentence
                                e.Code
                                "a wire-visible optionality"
                                "'hostOnly', a host slot with no wire projection")
                            "the refusal names why") ]

          testList
              "applying a delta"
              [ testCase "the base vocabulary is not mutated" (fun _ ->
                    // The whole branchless premise. `Idl` is immutable F#, so this
                    // cannot fail today — which is exactly why it is pinned: the
                    // day someone reaches for a mutable field, this is the test
                    // that says what breaks.
                    let before = Artifact.render Mini.miniIdl

                    Proposal.applyDelta
                        Mini.miniIdl
                        [ AddField(
                              OwnerKind "Badge",
                              { Name = "x"
                                Type = TStr
                                Opt = Optional
                                Aliases = []
                                Presence = None
                                Annotations = Annotations.Empty }
                          ) ]
                    |> ignore

                    Expect.equal
                        (Artifact.render Mini.miniIdl)
                        before
                        "applying a delta rendered the base vocabulary differently")

                testCase "a collision is refused rather than overwritten" (fun _ ->
                    match
                        Proposal.applyDelta
                            Mini.miniIdl
                            [ AddField(
                                  OwnerKind "Badge",
                                  { Name = "label"
                                    Type = TStr
                                    Opt = Optional
                                    Aliases = []
                                    Presence = None
                                    Annotations = Annotations.Empty }
                              ) ]
                    with
                    | Ok _ -> failtest "re-declaring an existing field was accepted"
                    | Error e -> Expect.stringContains e "already carries" "the refusal names the clash")

                testCase "an unknown owner is refused" (fun _ ->
                    match
                        Proposal.applyDelta
                            Mini.miniIdl
                            [ AddField(
                                  OwnerKind "NoSuchKind",
                                  { Name = "x"
                                    Type = TStr
                                    Opt = Optional
                                    Aliases = []
                                    Presence = None
                                    Annotations = Annotations.Empty }
                              ) ]
                    with
                    | Ok _ -> failtest "a field was added to a kind that does not exist"
                    | Error e -> Expect.stringContains e "does not exist" "the refusal names the missing owner")

                testCase "adding a kind that already exists is refused" (fun _ ->
                    match
                        Proposal.applyDelta
                            Mini.miniIdl
                            [ AddKind
                                  { Tag = "Badge"
                                    Category = "Display"
                                    Annotations = Annotations.Empty
                                    Fields = [] } ]
                    with
                    | Ok _ -> failtest "an existing kind was re-declared"
                    | Error e -> Expect.stringContains e "not additive" "the refusal says the delta is not additive") ]

          testList
              "the spike"
              [ testCase "a sound proposal spikes green on every leg" (fun _ ->
                    let r = spike (parseOrFail (completeJson tooltipDelta candidateWire))

                    Expect.isEmpty r.Defects "the document is complete"

                    let failed = r.Legs |> List.filter (fun l -> not l.Passed)

                    Expect.isEmpty
                        failed
                        (sprintf "legs failed: %A" (failed |> List.map (fun l -> l.Name + ": " + l.Detail)))

                    Expect.isTrue r.Green "green")

                testCase "the cost report is computed from the two revisions" (fun _ ->
                    let r = spike (parseOrFail (completeJson tooltipDelta candidateWire))
                    Expect.isFalse (r.CostReport.Contains "not computed") "a cost report was produced"
                    Expect.isGreaterThan r.CostReport.Length 0 "non-empty")

                testCase "a candidate that is ALREADY expressible fails the candidates leg" (fun _ ->
                    // The go-red that matters most: it is the check that catches a
                    // proposal answering demand the vocabulary already meets, which
                    // is the commonest way a proposal is wrong.
                    let r = spike (parseOrFail (completeJson tooltipDelta plainBadgeWire))

                    let l = legOf r "candidates"
                    Expect.isFalse l.Passed "an already-expressible candidate must fail the leg"
                    Expect.stringContains l.Detail "already expressible" "the failure says why"
                    Expect.isFalse r.Green "the report is not green")

                testCase "an empty corpus is reported as unchecked, never as a pass" (fun _ ->
                    let p = parseOrFail (completeJson tooltipDelta candidateWire)

                    let r =
                        ProposalSpike.run
                            { Base = Mini.miniIdl
                              Proposal = p
                              Corpus = []
                              FuzzSeed = 20260821
                              FuzzVectors = 50 }

                    let l = legOf r "corpus"
                    Expect.isFalse l.Passed "a vacuous leg is not a pass"
                    Expect.stringContains l.Detail "not checked" "and says so")

                testCase "a delta that does not apply reports one failing leg and no cost" (fun _ ->
                    let clash = tooltipDelta.Replace("\"name\": \"tooltip\"", "\"name\": \"label\"")

                    let r = spike (parseOrFail (completeJson clash candidateWire))

                    Expect.equal (List.length r.Legs) 1 "only the apply leg ran"
                    Expect.isFalse (legOf r "apply").Passed "apply failed"

                    Expect.stringContains
                        r.CostReport
                        "not computed"
                        "no cost was invented for a delta that did not apply"

                    Expect.isFalse r.Green "not green")

                testCase "the rendered report leads with defects, not with the verdict" (fun _ ->
                    let text =
                        (completeJson tooltipDelta candidateWire)
                            .Replace(
                                "\"irreducibility\": \"a hint is not a child node and has no composition that survives decode\"",
                                "\"irreducibility\": \"\""
                            )

                    let rendered = ProposalSpike.render (spike (parseOrFail text))

                    Expect.stringContains rendered "Document defects" "defects are surfaced"

                    Expect.isLessThan
                        (rendered.IndexOf "Document defects")
                        (rendered.IndexOf "## Legs")
                        "an incomplete argument is stated before the legs that cannot redeem it") ]

          testList
              "the spike-proposal command (Phase 230: the harness moved to the CLI, output unchanged)"
              [ testCase "it prints exactly the report the harness renders, and exits 0 on a green run" (fun _ ->
                    withCommandInputs (completeJson tooltipDelta candidateWire) (fun path ->
                        let expected =
                            ProposalSpike.run
                                { Base = Mini.miniIdl
                                  Proposal = parseOrFail (completeJson tooltipDelta candidateWire)
                                  Corpus = [ "plain-badge.json", plainBadgeWire ]
                                  FuzzSeed = commandSeed
                                  FuzzVectors = commandVectors }

                        Expect.isTrue
                            expected.Green
                            "the fixture proposal is a clean one, so the exit below means something"

                        let code, out, _ =
                            runCli (
                                sprintf
                                    "spike-proposal \"%s\" --idl \"%s\" --corpus \"%s\""
                                    (path "proposal.json")
                                    (path "idl.json")
                                    (path "corpus")
                            )

                        Expect.equal code 0 "every leg passed, so exit 0"

                        Expect.equal
                            out
                            (ProposalSpike.render expected)
                            "the command prints the harness report, byte for byte"))

                testCase "a failing leg exits 1" (fun _ ->
                    // The candidate is already expressible before the delta, so the candidates leg fails.
                    withCommandInputs (completeJson tooltipDelta plainBadgeWire) (fun path ->
                        let code, out, _ =
                            runCli (
                                sprintf
                                    "spike-proposal \"%s\" --idl \"%s\" --corpus \"%s\""
                                    (path "proposal.json")
                                    (path "idl.json")
                                    (path "corpus")
                            )

                        Expect.equal code 1 "a failed leg is exit 1"
                        Expect.stringContains out "already expressible" "and the report says which leg and why"))

                // Phase 526 — the tool's ONE refusal channel is stdout, `classify`'s; the verb's refusals
                // used to go to stderr, and these assertions pin the move in both directions.
                testCase "no --idl is a refusal: exit 2, said on the tool's one refusal channel (stdout)" (fun _ ->
                    withCommandInputs (completeJson tooltipDelta candidateWire) (fun path ->
                        let code, out, err = runCli (sprintf "spike-proposal \"%s\"" (path "proposal.json"))

                        Expect.equal code 2 "the document did not read is exit 2"
                        Expect.stringContains out "fuaran-core-idl: no --idl" "the refusal names the missing argument"
                        Expect.equal err "" "and nothing is printed to stderr"))

                testCase
                    "Phase 384 — a bad flag, a bad integer, a missing file and an unwritable --out are typed refusals, exit 2"
                    (fun _ ->
                        withCommandInputs (completeJson tooltipDelta candidateWire) (fun path ->
                            let full =
                                sprintf
                                    "spike-proposal \"%s\" --idl \"%s\" --corpus \"%s\""
                                    (path "proposal.json")
                                    (path "idl.json")
                                    (path "corpus")

                            for args, named in
                                [ full + " --sede 7", "unrecognised option: --sede"
                                  full + " --seed seven", "--seed takes an integer, not 'seven'"
                                  full + " --vectors 1.5", "--vectors takes an integer, not '1.5'"
                                  full + " --vectors", "--vectors needs an integer"
                                  sprintf
                                      "spike-proposal \"%s\" --idl \"%s\""
                                      (path "absent-proposal.json")
                                      (path "idl.json"),
                                  "proposal not found"
                                  sprintf
                                      "spike-proposal \"%s\" --idl \"%s\""
                                      (path "proposal.json")
                                      (path "absent-idl.json"),
                                  "--idl names no file"
                                  sprintf
                                      "spike-proposal \"%s\" --idl \"%s\" --corpus \"%s\""
                                      (path "proposal.json")
                                      (path "idl.json")
                                      (path "absent-corpus"),
                                  "--corpus names no directory"
                                  full + sprintf " --out \"%s\"" (path "no-such-dir/report.md"), "--out unwritable" ] do
                                let code, out, err = runCli args
                                Expect.equal code 2 (sprintf "%s: refused, exit 2" named)
                                Expect.stringContains out named (sprintf "%s: the refusal names it" named)

                                Expect.isFalse
                                    (out.Contains "   at " || out.Contains "Exception")
                                    (sprintf "%s: a sentence, never a stack trace — got %s" named out)

                                Expect.equal err "" (sprintf "%s: nothing on stderr" named)

                            // The control: the same full invocation, with a writable --out, is green.
                            let code, out, _ = runCli (full + sprintf " --out \"%s\"" (path "report.md"))
                            Expect.equal code 0 "a writable --out on a green run exits 0"
                            Expect.stringContains out "wrote" "and says where it wrote"))

                testCase "the verb is in the help text, and a bare verb is refused" (fun _ ->
                    let helpCode, helpOut, _ = runCli "--help"
                    Expect.equal helpCode 0 "help exits 0"
                    Expect.stringContains helpOut "spike-proposal" "the command lists the verb"

                    let bareCode, _, _ = runCli "spike-proposal"
                    Expect.equal bareCode 2 "a verb with no document is refused")

                // Phase 407 - the bytes, not the decode. A decoded string is only evidence about the
                // CLI when the decode is the right one; reading the raw pipe asks what the process
                // wrote, so the console code page of the machine cannot change the answer.
                testCase
                    "the help text is UTF-8 bytes with no byte-order mark, whatever the console code page"
                    (fun _ ->
                        let p =
                            ChildProcess.run (ChildProcess.command "dotnet" ("\"" + cliDll + "\" --help"))
                            |> Result.defaultWith failwith

                        let bytes = p.StdoutBytes
                        Expect.equal p.Exit 0 "help exits 0"
                        Expect.isGreaterThan bytes.Length 3 "the help text is not empty"

                        Expect.notEqual
                            (Array.truncate 3 bytes)
                            [| 0xEFuy; 0xBBuy; 0xBFuy |]
                            "no byte-order mark leads the output"

                        let emDash = System.Text.Encoding.UTF8.GetBytes "—"
                        Expect.equal emDash [| 0xE2uy; 0x80uy; 0x94uy |] "the em dash is E2 80 94 in UTF-8"

                        let contains = bytes |> Array.windowed 3 |> Array.exists (fun w -> w = emDash)

                        Expect.isTrue contains "the em dash of the usage heading is written as its UTF-8 sequence"

                        let decoded = System.Text.UTF8Encoding(false, true).GetString bytes
                        Expect.stringContains decoded "fuaran-core-idl — the IDL" "the bytes decode strictly as UTF-8") ] ]

// ---------------------------------------------------------------------------
// Phase 527 — a proposal is read by the artifact's codecs under an admission policy.
//
// One reader of the encoding, and the refusals kept: what a proposal may not mint is
// `NotAdmitted` at the member that mints it, wherever it sits in the type. And a proposal may
// carry the doc of what it mints (the 2026-10-01 generator follow-ups' item 3), on a field and
// on a new kind or union case, and nothing else an annotation set says.
// ---------------------------------------------------------------------------

let private refusalOf (text: string) : DecodeCode * PathSegment list =
    match Proposal.parseDetailed text with
    | Ok _ -> failtest "the document was admitted"
    | Error e -> e.Code, e.Path

let private fieldPath (rest: PathSegment list) : PathSegment list =
    [ PathSegment.Key "delta"; PathSegment.Index 0; PathSegment.Key "field" ] @ rest

let private withField (fieldJson: string) : string =
    completeJson
        (sprintf """{ "op": "addField", "owner": { "$type": "kind", "name": "Badge" }, "field": %s }""" fieldJson)
        candidateWire

[<Tests>]
let admissionTests =
    testList
        "Phase 527 — proposals read through the artifact, under admit"
        [ testCase "a host-surface type nested in a list is refused at its own tag" (fun _ ->
              Expect.equal
                  (refusalOf (
                      withField
                          """{ "name": "hooks", "type": { "$type": "list", "of": { "$type": "opaque" } }, "optionality": { "$type": "optional" } }"""
                  ))
                  (DecodeCode.NotAdmitted,
                   fieldPath [ PathSegment.Key "type"; PathSegment.Key "of"; PathSegment.Key "$type" ])
                  "NotAdmitted at the nested tag")

          testCase "a composite default is refused at the default's tag" (fun _ ->
              Expect.equal
                  (refusalOf (
                      withField
                          """{ "name": "tags", "type": { "$type": "list", "of": { "$type": "str" } }, "optionality": { "$type": "omitDefault", "default": { "$type": "list", "items": [] } } }"""
                  ))
                  (DecodeCode.NotAdmitted,
                   fieldPath
                       [ PathSegment.Key "optionality"
                         PathSegment.Key "default"
                         PathSegment.Key "$type" ])
                  "NotAdmitted at the default")

          testCase "a field may carry its doc, and the doc reaches the vocabulary" (fun _ ->
              let p =
                  parseOrFail (
                      withField
                          """{ "name": "tooltip", "type": { "$type": "str" }, "optionality": { "$type": "optional" }, "annotations": { "doc": "A hover hint." } }"""
                  )

              match p.Delta with
              | [ AddField(_, f) ] -> Expect.equal f.Annotations.Doc (Some "A hover hint.") "the doc is read"
              | other -> failtestf "one field addition: %A" other

              match Proposal.applyDelta Mini.miniIdl p.Delta with
              | Ok idl ->
                  let badge = idl.Kinds |> List.find (fun k -> k.Tag = "Badge")
                  let tooltip = badge.Fields |> List.find (fun f -> f.Name = "tooltip")

                  Expect.equal
                      tooltip.Annotations.Doc
                      (Some "A hover hint.")
                      "and is carried by the post-delta vocabulary"
              | Error e -> failtestf "the delta did not apply: %s" e)

          testCase "any other annotation on a minted member is refused at that annotation" (fun _ ->
              for name, block in
                  [ "deprecated", """{ "deprecated": {} }"""
                    "inProcessOnly", """{ "inProcessOnly": true }"""
                    "since", """{ "since": "2" }""" ] do
                  Expect.equal
                      (refusalOf (
                          withField (
                              """{ "name": "tooltip", "type": { "$type": "str" }, "optionality": { "$type": "optional" }, "annotations": """
                              + block
                              + " }"
                          )
                      ))
                      (DecodeCode.NotAdmitted, fieldPath [ PathSegment.Key "annotations"; PathSegment.Key name ])
                      (sprintf "'%s' is not admitted" name))

          testCase "a new kind may carry its doc" (fun _ ->
              let p =
                  parseOrFail (
                      completeJson
                          """{ "op": "addKind", "kind": { "tag": "Hint", "annotations": { "doc": "A hint." }, "fields": [] } }"""
                          candidateWire
                  )

              match p.Delta with
              | [ AddKind k ] -> Expect.equal k.Annotations.Doc (Some "A hint.") "the kind's doc is read"
              | other -> failtestf "one kind addition: %A" other) ]
