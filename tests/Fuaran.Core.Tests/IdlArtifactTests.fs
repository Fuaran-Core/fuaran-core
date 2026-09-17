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
// This phase changes the WRITER only. A freshly rendered artifact now declares its
// policy outright at every policy value, so no reader has to infer it; the READER is
// untouched, because the artifacts written before this phase still exist. Flipping the
// reader is Phase 180, gated on those two artifacts having been re-rendered here.
//
// Four families below, and the fourth is the one that earns the "additive" class rather
// than asserting it: the diff classifier is run over the old bytes and the new, and
// reports no hardening change at all.
//
// `IdlTrustTests`' compat family is the sibling guard on the reader half — it holds the
// D40 promise itself. This file holds the writer half and the round trip.
// ---------------------------------------------------------------------------

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
/// hard-code, the Phase-178 opt-in that declares none of them, and a vocabulary that
/// spells its own — including a transparent union, the one wire-visible member.
let private policies: (string * HardenPolicy) list =
    [ "Default", HardenPolicy.Default
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

                // The `Default` case stated on its own, because it is the one that
                // CHANGED and the one a later reader will come looking for. Before this
                // phase the assertion beside it was `isFalse`.
                testCase "the default is no longer the omitted case" (fun _ ->
                    let text = Artifact.render (at HardenPolicy.Default)

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
              [ // Phase 178's opt-in says "I have not named these", and on the wire that
                // has to be the explicit empty members it is — not an absent block, which
                // by `readHarden`'s standing promise means the exact opposite. Pinned as
                // bytes, because this is the one policy whose rendering could plausibly
                // be "optimised" back to an omission by someone reading empty strings as
                // nothing to say.
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
                        Expect.equal back.Harden HardenPolicy.Undeclared "the opt-in survives the round trip"

                        Expect.notEqual
                            back.Harden
                            HardenPolicy.Default
                            "and is still a different claim from the default") ]

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

                // The compat law from 178, restated from THIS phase's side: the writer
                // moved, the reader did not, so bytes written by the previous version
                // still mean what they meant. If this goes red, every artifact published
                // before this phase has changed meaning — see D40 before touching it.
                testCase "an artifact rendered by the PREVIOUS version still parses to Default" (fun _ ->
                    let old = withoutHarden (at HardenPolicy.Default)

                    Expect.isFalse (old.Contains "\"harden\"") "the fixture is what a pre-179 renderer wrote"

                    match Artifact.parse old with
                    | Error m -> failtestf "the pre-179 artifact did not parse: %s" m
                    | Ok back ->
                        Expect.equal
                            back.Harden
                            HardenPolicy.Default
                            "an absent block still reads as Default — NOT as Undeclared"

                        Expect.notEqual
                            back.Harden
                            HardenPolicy.Undeclared
                            "if this ever fails, every published artifact's hardening has changed meaning")

                // Both directions of the migration's one step: re-rendering a pre-179
                // artifact ADDS the block and changes nothing else. This is the shape
                // Phase 180's gate is waiting on, asserted here rather than described.
                testCase "re-rendering a pre-179 artifact adds the block and nothing else" (fun _ ->
                    let idl = at HardenPolicy.Default

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
                // writes, it must report NO hardening change — the block's presence at
                // the default says exactly what its absence did.
                //
                // Run in both directions, because a classifier that reported nothing
                // for an unrelated reason would look identical from one side.
                testCase "the diff between pre-179 and post-179 bytes reports no harden change" (fun _ ->
                    let idl = at HardenPolicy.Default
                    let before = withoutHarden idl
                    let after = Artifact.render idl

                    Expect.notEqual before after "the bytes did move — otherwise this proves nothing"

                    for label, a, b in [ "forwards", before, after; "backwards", after, before ] do
                        match Diff.classifyArtifacts a b with
                        | Error m -> failtestf "'%s': the classifier refused the pair: %s" label m
                        | Ok verdict ->
                            Expect.isEmpty
                                (verdict.Changes |> List.filter hardenRow)
                                (sprintf "'%s': adding the block at the default is not a hardening change" label))

                // The falsifier for the case above: a policy that genuinely moved MUST
                // be reported. Without this the test above passes on a classifier that
                // reports nothing at all.
                testCase "a policy that genuinely moved IS reported" (fun _ ->
                    let before = Artifact.render (at HardenPolicy.Default)
                    let after = Artifact.render (at HardenPolicy.Undeclared)

                    match Diff.classifyArtifacts before after with
                    | Error m -> failtestf "the classifier refused the pair: %s" m
                    | Ok verdict ->
                        Expect.isNonEmpty
                            (verdict.Changes |> List.filter hardenRow)
                            "emptying every token is a hardening change and must be reported") ] ]
