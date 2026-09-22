module Fuaran.Core.Tests.IdlArtifactTests

open Expecto
open Fuaran.Core
open Fuaran.Core.Idl
open Fuaran.Core.Tests.ReferenceIdl

// ---------------------------------------------------------------------------
// Phase 179 — the artifact always carries its `harden` block.
//
// Step ONE of the two-step wire migration D40 laid out, and the whole reason the
// retirement of `HardenPolicy.Default` needs two steps: the default was a WIRE fact.
// `Artifact.render` omitted the block exactly at the default and `Artifact.readHarden`
// resolves an absent block back through it, so "absent" MEANT one domain's tokens for
// every host reading the two published `idl.json` artifacts. Emptying the default in
// place would have changed what already-published bytes mean, silently, with a green
// build — which is what Phase 178 measured and refused.
//
// That phase changed the WRITER only. A freshly rendered artifact declares its policy
// outright at every policy value, so no reader has to infer it; the READER stayed put,
// because the artifacts written before it still existed.
//
// **Phase 180 flipped the reader, and this file is amended rather than rewritten.** The
// gate D40 named is met — `fuaran#1755` re-rendered both published artifacts (`bb10065`)
// and `roadmapctl copies` reports the corpus and both bundled host snapshots in step —
// so `HardenPolicy.Default` is deleted and an absent `harden` block reads back as
// `Undeclared`. Three consequences here, all of them deliberate:
//
//   - the old `Default` fixture is now a LITERAL token set (`retiredDefaultTokens`),
//     because the claim "the writer omits nothing, whatever the policy" outlives the
//     record member that used to supply one of the policies;
//   - the compat case INVERTS — a pre-179 artifact parses to `Undeclared`, and the
//     case below says why that is safe rather than merely different;
//   - the additive measurement re-anchors on the policy where it is still TRUE: absent
//     and all-empty now mean the same thing, so adding the block to an artifact that
//     declared nothing is still not a hardening change, and the falsifier beside it
//     requires a genuine move to be reported.
//
// `IdlTrustTests`' family is the sibling guard on the reader half. This file holds the
// writer half and the round trip.
// ---------------------------------------------------------------------------

/// The five tokens the engine hard-coded before Phase 116 made them declarable, shipped
/// as `HardenPolicy.Default` between 116 and 180 and deleted by 180. Spelled out here
/// because the tests that used to read them off that member are testing the WRITER and
/// the CLASSIFIER, neither of which cared which names it was handed — only that a
/// fully-declared policy including the one wire-visible member round-trips.
let private retiredDefaultTokens: HardenPolicy =
    { GatedKind = "Custom"
      PlaceholderKind = "Markdown"
      PlaceholderField = "text"
      TextLiteralCase = "Literal"
      TextLiteralField = "text"
      ValueLiteralCase = "Static"
      ValueLiteralField = "value"
      TransparentUnions = [ "TextSource", "Literal" ] }

/// The bytes a PRE-Phase-179 renderer produced for this vocabulary: the same artifact
/// with the `harden` block dropped at the JSON level.
///
/// Built through [[Artifact.json]] and [[Artifact.renderJson]] rather than by text
/// surgery on the rendered string, so it stays a well-formed artifact if the layout,
/// the key order or the block's position ever moves — and goes red loudly, as a shape
/// failure, rather than quietly producing bytes no renderer ever wrote.
let private withoutHarden (idl: Idl) : string =
    match Artifact.json idl with
    | JObj members ->
        let stripped = members |> List.filter (fun (k, _) -> k <> "harden")

        Expect.equal
            (List.length stripped)
            (List.length members - 1)
            "the artifact root carries exactly one `harden` member to strip"

        Artifact.renderJson (JObj stripped)
    | other -> failtestf "the artifact root is not a JSON object: %A" other

/// Every policy value the phase has to answer for: the tokens the engine used to
/// hard-code, the Phase-178 policy that declares none of them, and a vocabulary that
/// spells its own — including a transparent union, the one wire-visible member.
let private policies: (string * HardenPolicy) list =
    [ "the retired default's tokens", retiredDefaultTokens
      "Undeclared", HardenPolicy.Undeclared
      "the reference vocabulary's own", refIdl.Harden
      "its own, with a transparent union",
      { refIdl.Harden with
          TransparentUnions = [ "Text", "Inline" ] } ]

let private at (policy: HardenPolicy) : Idl = { refIdl with Harden = policy }

/// A diff row reporting that the declared hardening vocabulary moved.
let private hardenRow (c: Diff.Classification) =
    match c.Change with
    | Diff.HardenPolicyChanged _ -> true
    | _ -> false

[<Tests>]
let tests =
    testList
        "Phase 179 — the artifact always carries its harden block"
        [ testList
              "the block is emitted unconditionally"
              [ for name, policy in policies do
                    testCase (sprintf "a vocabulary at %s renders the block" name) (fun _ ->
                        let text = Artifact.render (at policy)

                        Expect.stringContains
                            text
                            "\"harden\""
                            (sprintf "'%s': a freshly rendered artifact declares its policy outright" name))

                // The case stated on its own, because it is the one Phase 179 CHANGED and
                // the one a later reader will come looking for. Before that phase the
                // assertion beside it was `isFalse`, and these exact tokens were the
                // policy whose rendering was omitted.
                testCase "the retired default's tokens are no longer the omitted case" (fun _ ->
                    let text = Artifact.render (at retiredDefaultTokens)

                    Expect.stringContains
                        text
                        "\"gatedKind\": \"Custom\""
                        "the default's tokens are written out rather than left to be inferred"

                    Expect.stringContains
                        text
                        "\"transparentUnions\""
                        "including the one member a WIRE consumer must read") ]

          testList
              "the render of Undeclared, pinned"
              [ // `Undeclared` says "I have not named these", and on the wire that is the
                // explicit empty members it is. Pinned as bytes, because this is the one
                // policy whose rendering could plausibly be "optimised" back to an
                // omission by someone reading empty strings as nothing to say — and since
                // Phase 180 an omission decodes to this same policy, so such an
                // optimisation would be invisible to a round-trip test and visible only
                // here.
                testCase "every member renders as the empty declaration it is" (fun _ ->
                    let text = Artifact.render (at HardenPolicy.Undeclared)

                    for member_ in
                        [ "gatedKind"
                          "placeholderKind"
                          "placeholderField"
                          "textLiteralCase"
                          "textLiteralField"
                          "valueLiteralCase"
                          "valueLiteralField" ] do
                        Expect.stringContains
                            text
                            (sprintf "\"%s\": \"\"" member_)
                            (sprintf "'%s' is declared empty, not omitted" member_)

                    Expect.stringContains text "\"transparentUnions\": []" "and the declared-empty transparent set"

                    match Artifact.parse text with
                    | Error m -> failtestf "the undeclared artifact did not parse: %s" m
                    | Ok back ->
                        Expect.equal back.Harden HardenPolicy.Undeclared "the declaration survives the round trip"

                        Expect.notEqual
                            back.Harden
                            retiredDefaultTokens
                            "and is still a different claim from the tokens the engine used to supply") ]

          testList
              "render → parse → render, with the block"
              [ for name, policy in policies do
                    testCase (sprintf "%s is byte-identical across the round trip" name) (fun _ ->
                        let idl = at policy
                        let text = Artifact.render idl

                        match Artifact.parse text with
                        | Error m -> failtestf "'%s': the artifact did not parse: %s" name m
                        | Ok back ->
                            Expect.equal (Artifact.render back) text (sprintf "'%s': the bytes are not stable" name)

                            Expect.equal
                                back.Harden
                                policy
                                (sprintf "'%s': the policy survived the round trip exactly" name))

                // Phase 180 — the compat law from 178 is INVERTED here, on purpose, and
                // this case is the record of it. The law was never "an absent block means
                // the engine's old tokens"; it was "an absent block must not change
                // meaning while artifacts relying on that meaning exist". Phase 179 made
                // the writer emit the block always, `fuaran#1755` re-rendered the two
                // published artifacts, and `roadmapctl copies` reports both bundled host
                // snapshots of the shared corpus in step — so the set of artifacts the
                // flip could change the meaning of is empty, which is the condition D40
                // named and the only thing that ever gated it.
                //
                // An artifact that would still hit this path — rendered before 179 and
                // never re-rendered — now decodes to a vocabulary that REFUSES to harden
                // rather than one that hardens as a domain it never named. That is the
                // safe direction of the two, which is why it is the one taken.
                testCase "an artifact rendered by the PREVIOUS version parses to Undeclared" (fun _ ->
                    let old = withoutHarden (at retiredDefaultTokens)

                    Expect.isFalse (old.Contains "\"harden\"") "the fixture is what a pre-179 renderer wrote"

                    match Artifact.parse old with
                    | Error m -> failtestf "the pre-179 artifact did not parse: %s" m
                    | Ok back ->
                        Expect.equal
                            back.Harden
                            HardenPolicy.Undeclared
                            "an absent block reads as Undeclared — Phase 180's inversion"

                        Expect.notEqual
                            back.Harden
                            retiredDefaultTokens
                            "the tokens the engine used to supply are not supplied by anything now")

                // The consequence, stated as bytes: re-rendering a pre-179 artifact is no
                // longer the identity on meaning. It ADDS a block declaring nothing, where
                // before 180 it added a block declaring the engine's tokens. Asserting the
                // difference is what keeps it a decision rather than an accident — and it
                // is exactly why `fuaran#1755` had to re-render the published artifacts
                // BEFORE this phase, not after.
                testCase "re-rendering a pre-179 artifact now yields the undeclared block" (fun _ ->
                    let idl = at retiredDefaultTokens

                    match Artifact.parse (withoutHarden idl) with
                    | Error m -> failtestf "the pre-179 artifact did not parse: %s" m
                    | Ok back ->
                        Expect.equal
                            (Artifact.render back)
                            (Artifact.render (at HardenPolicy.Undeclared))
                            "the re-render declares nothing, because the artifact declared nothing"

                        Expect.notEqual
                            (Artifact.render back)
                            (Artifact.render idl)
                            "and is NOT the artifact the tokens would have produced — the meaning moved, deliberately")

                // The same step over an artifact that was ALREADY declaring nothing, which
                // is the case the inversion leaves untouched: absent and all-empty now say
                // the same thing, so the re-render is the identity on meaning.
                testCase "re-rendering a blockless artifact of an undeclared vocabulary is stable" (fun _ ->
                    let idl = at HardenPolicy.Undeclared

                    match Artifact.parse (withoutHarden idl) with
                    | Error m -> failtestf "the pre-179 artifact did not parse: %s" m
                    | Ok back ->
                        Expect.equal
                            (Artifact.render back)
                            (Artifact.render idl)
                            "the re-render is the current artifact, block included") ]

          testList
              "the additive class, measured rather than asserted"
              [ // The STABILITY entry calls this additive on the wire. The diff
                // classifier is what the estate reads that claim through, so run it:
                // between the bytes a pre-179 renderer wrote and the bytes this one
                // writes, it must report NO hardening change — the block's presence says
                // exactly what its absence did.
                //
                // **Re-anchored by Phase 180, onto the policy where the claim is still
                // true.** It used to run at the engine's old token set, because that was
                // what an absent block meant; an absent block now means `Undeclared`, so
                // that is the policy whose block adds nothing. The measurement is the same
                // measurement — it is the definition of "absent" underneath it that moved,
                // which is the whole content of this phase.
                //
                // Run in both directions, because a classifier that reported nothing
                // for an unrelated reason would look identical from one side.
                testCase "the diff between pre-179 and post-179 bytes reports no harden change" (fun _ ->
                    let idl = at HardenPolicy.Undeclared
                    let before = withoutHarden idl
                    let after = Artifact.render idl

                    Expect.notEqual before after "the bytes did move — otherwise this proves nothing"

                    for label, a, b in [ "forwards", before, after; "backwards", after, before ] do
                        match Diff.classifyArtifacts a b with
                        | Error m -> failtestf "'%s': the classifier refused the pair: %s" label m
                        | Ok verdict ->
                            Expect.isEmpty
                                (verdict.Changes |> List.filter hardenRow)
                                (sprintf "'%s': adding an all-empty block is not a hardening change" label))

                // The falsifier for the case above: a policy that genuinely moved MUST
                // be reported. Without this the test above passes on a classifier that
                // reports nothing at all.
                testCase "a policy that genuinely moved IS reported" (fun _ ->
                    let before = Artifact.render (at retiredDefaultTokens)
                    let after = Artifact.render (at HardenPolicy.Undeclared)

                    match Diff.classifyArtifacts before after with
                    | Error m -> failtestf "the classifier refused the pair: %s" m
                    | Ok verdict ->
                        Expect.isNonEmpty
                            (verdict.Changes |> List.filter hardenRow)
                            "emptying every token is a hardening change and must be reported") ] ]
