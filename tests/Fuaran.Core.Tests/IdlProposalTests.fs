module Fuaran.Core.Tests.IdlProposalTests

open Expecto
open Fuaran.Core
open Fuaran.Core.Idl

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

module Mini = Fuaran.Core.Idl.Spike.Fixtures

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
    match
        ProposalSpike.run
            { Base = Mini.miniIdl
              Proposal = p
              Corpus = [ "plain-badge", plainBadgeWire ]
              FuzzSeed = 20260821
              FuzzVectors = 200
              External = [] }
    with
    | Ok r -> r
    | Error e -> failtestf "spike did not run: %s" e

let private legOf (r: SpikeReport) (name: string) =
    match r.Legs |> List.tryFind (fun l -> l.Name = name) with
    | Some l -> l
    | None -> failtestf "no '%s' leg in the report (legs: %A)" name (r.Legs |> List.map (fun l -> l.Name))

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

                    match Proposal.parse (completeJson hostDelta candidateWire) with
                    | Ok _ -> failtest "a closure slot was accepted from a proposal document"
                    | Error e -> Expect.stringContains e "host-surface" "the refusal names why")

                testCase "a host-only optionality cannot be minted by a proposal" (fun _ ->
                    let hostOnly =
                        tooltipDelta.Replace("{ \"$type\": \"optional\" }", "{ \"$type\": \"hostOnly\" }")

                    match Proposal.parse (completeJson hostOnly candidateWire) with
                    | Ok _ -> failtest "a wire-invisible slot was accepted from a proposal document"
                    | Error e -> Expect.stringContains e "hostOnly" "the refusal names why") ]

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
                        match
                            ProposalSpike.run
                                { Base = Mini.miniIdl
                                  Proposal = p
                                  Corpus = []
                                  FuzzSeed = 20260821
                                  FuzzVectors = 50
                                  External = [] }
                        with
                        | Ok r -> r
                        | Error e -> failtestf "spike did not run: %s" e

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
                        "an incomplete argument is stated before the legs that cannot redeem it") ] ]
