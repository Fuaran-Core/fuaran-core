module Fuaran.Core.Tests.IdlTrustTests

open Expecto
open Fuaran.Core
open Fuaran.Core.Idl
open Fuaran.Core.Tests.ReferenceIdl

// ---------------------------------------------------------------------------
// Phase 178 — the hardening policy's UNDECLARED half, and the measurement that kept
// it an opt-in.
//
// The phase was written to flip `HardenPolicy`'s default from the four tokens the
// engine used to hard-code (`Custom` / `Markdown` / `Static` / `TextSource.Literal`,
// all of them one domain's spelling) to "declared nothing, so say so". Its licensing
// premise was that every vocabulary in the estate already declares its own tokens, so
// the flip would be breaking on paper and land on no consumer.
//
// Measured before the flip, that premise is false in the two places that decide it —
// `DECISIONS.md` D40 carries the list. The refusal therefore shipped REACHABLE and
// OPT-IN: `HardenPolicy.Undeclared` beside `Default`, `Trust.checkHardenPolicy` and
// `Trust.hardenOrRefuse` beside `harden`, and nothing in the existing path changed.
//
// **Phase 180 took the flip, on the schedule the measurement asked for.** Phase 179 made
// the writer emit the block for every policy; `fuaran#1755` re-rendered both published
// artifacts so neither depends on the absent-block answer; and only then did `Default`
// go. `Trust.harden` is the checked path now — `hardenOrRefuse` survives as its alias —
// and an absent `harden` block reads back as `Undeclared`. The compat family below is
// therefore INVERTED rather than deleted, and the inversion is asserted with its reason
// beside it: the law was never "an absent block means the UI's tokens", it was "an
// absent block must not change meaning while artifacts relying on that meaning exist".
//
// Two families below. The first pins the refusal per member, each case going red on its
// own. The second pins the reader answer in its new direction, and names what had to be
// true before it could move.
// ---------------------------------------------------------------------------

let private noTrust: Trust.Policy =
    { Allowlist = []
      UrlFields = Set.empty
      MarkdownFields = Set.empty }

let private withUrlField: Trust.Policy =
    { noTrust with
        UrlFields = Set.ofList [ "Link", "href" ] }

/// `refIdl`'s own policy with exactly ONE member emptied — the go-red shape: each case
/// below differs from the fully-declared vocabulary in one member and no other.
let private missing (name: string) : Idl =
    let h = refIdl.Harden

    let blanked =
        match name with
        | "GatedKind" -> { h with GatedKind = "" }
        | "PlaceholderKind" -> { h with PlaceholderKind = "" }
        | "PlaceholderField" -> { h with PlaceholderField = "" }
        | "TextLiteralCase" -> { h with TextLiteralCase = "" }
        | "TextLiteralField" -> { h with TextLiteralField = "" }
        | "ValueLiteralCase" -> { h with ValueLiteralCase = "" }
        | "ValueLiteralField" -> { h with ValueLiteralField = "" }
        | other -> failtestf "no such HardenPolicy member: %s" other

    { refIdl with Harden = blanked }

[<Tests>]
let tests =
    testList
        "Phase 178 — the undeclared hardening policy"
        [ testList
              "the refusal, per member"
              [ // The five the GATE needs unconditionally: the gate runs over every
                // harden, and its inert placeholder is built from the other four. A
                // vocabulary that leaves any of them empty is not "not using the
                // gate" — an undeclared `GatedKind` matches no node tag, so the run
                // would gate NOTHING and say nothing about it.
                for member_ in
                    [ "GatedKind"
                      "PlaceholderKind"
                      "PlaceholderField"
                      "TextLiteralCase"
                      "TextLiteralField" ] do
                    testCase (sprintf "an undeclared '%s' refuses, naming the member" member_) (fun _ ->
                        match Trust.checkHardenPolicy (missing member_) noTrust with
                        | Error(CodegenError.UndeclaredHardenToken(named, needed)) ->
                            Expect.equal named member_ "the refusal names the member that is undeclared"

                            Expect.isNonEmpty needed "the refusal names what needed it"

                            Expect.stringContains
                                (CodegenError.describe (CodegenError.UndeclaredHardenToken(named, needed)))
                                member_
                                "the rendered refusal names the member too"
                        | Error other -> failtestf "refused, but with the wrong case: %A" other
                        | Ok() -> failtestf "'%s' is undeclared and the run needs it — this must refuse" member_)

                // The two the URL sanitiser needs, and ONLY when the caller declared a
                // URL field. Run in BOTH directions: a probe that only checked the
                // refusing side would pass identically if the members were
                // unconditionally required, which is a different (and wrong) contract.
                for member_ in [ "ValueLiteralCase"; "ValueLiteralField" ] do
                    testCase (sprintf "'%s' is needed exactly when a URL field is declared" member_) (fun _ ->
                        match Trust.checkHardenPolicy (missing member_) withUrlField with
                        | Error(CodegenError.UndeclaredHardenToken(named, _)) ->
                            Expect.equal named member_ "the refusal names the member that is undeclared"
                        | Error other -> failtestf "refused, but with the wrong case: %A" other
                        | Ok() -> failtest "a declared URL field needs the value-literal members"

                        Expect.isOk
                            (Trust.checkHardenPolicy (missing member_) noTrust)
                            "with no URL field declared, the value-literal members are not needed") ]

          testList
              "what does NOT refuse"
              [ testCase "the reference vocabulary's own fully-declared policy passes" (fun _ ->
                    Expect.isOk (Trust.checkHardenPolicy refIdl noTrust) "every member is declared"
                    Expect.isOk (Trust.checkHardenPolicy refIdl withUrlField) "including the URL pair")

                // Phase 180 — this case used to assert that `HardenPolicy.Default` never
                // refuses, which was the measurement's direct consequence while the
                // default existed. The default is deleted; the claim underneath it is
                // not, and is worth more stated directly: the check keys on a member
                // being EMPTY, never on which name it holds. So the retired default's
                // own five tokens — spelled here, since nothing in the engine spells
                // them any more — pass exactly as `refIdl`'s unrelated ones do.
                testCase "the check keys on emptiness, not on spelling" (fun _ ->
                    let retiredDefaultTokens =
                        { GatedKind = "Custom"
                          PlaceholderKind = "Markdown"
                          PlaceholderField = "text"
                          TextLiteralCase = "Literal"
                          TextLiteralField = "text"
                          ValueLiteralCase = "Static"
                          ValueLiteralField = "value"
                          TransparentUnions = [ "TextSource", "Literal" ] }

                    let onRetired =
                        { refIdl with
                            Harden = retiredDefaultTokens }

                    Expect.isOk (Trust.checkHardenPolicy onRetired noTrust) "every member is declared"
                    Expect.isOk (Trust.checkHardenPolicy onRetired withUrlField) "including the URL pair")

                // An empty `TransparentUnions` is a DECLARATION, not an absence — it is
                // how a vocabulary no case of which encodes bare says so, and `refIdl`
                // says exactly that on purpose. Refusing it would make that vocabulary
                // unexpressible.
                testCase "an empty TransparentUnions is a declaration, not an omission" (fun _ ->
                    Expect.isEmpty refIdl.Harden.TransparentUnions "the fixture declares no transparent union"
                    Expect.isOk (Trust.checkHardenPolicy refIdl noTrust) "and that is not a refusal")

                // Phase 180 — `harden` IS the checked path, and `hardenOrRefuse` is the
                // name Phase 178 shipped it under, kept so code written between the two
                // phases still compiles. The alias is asserted rather than assumed: a
                // later session that gave the two different behaviours would be
                // reintroducing exactly the unchecked entry point 180 removed.
                testCase "hardenOrRefuse is an alias of harden, on both outcomes" (fun _ ->
                    let v = VNode("n-1", "Note", [ "body", VUnion("Inline", [ "text", VStr "hello" ]) ])

                    Expect.equal
                        (Trust.hardenOrRefuse refIdl noTrust v)
                        (Trust.harden refIdl noTrust v)
                        "the two names agree on a declared vocabulary"

                    let undeclared =
                        { refIdl with
                            Harden = HardenPolicy.Undeclared }

                    Expect.equal
                        (Trust.hardenOrRefuse undeclared noTrust v)
                        (Trust.harden undeclared noTrust v)
                        "and on an undeclared one")

                testCase "harden refuses an undeclared vocabulary before hardening" (fun _ ->
                    let v = VNode("n-1", "Note", [ "body", VUnion("Inline", [ "text", VStr "hello" ]) ])

                    match
                        Trust.harden
                            { refIdl with
                                Harden = HardenPolicy.Undeclared }
                            noTrust
                            v
                    with
                    | Error(CodegenError.UndeclaredHardenToken _) -> ()
                    | Error other -> failtestf "refused, but with the wrong case: %A" other
                    | Ok _ -> failtest "an undeclared policy must refuse rather than harden through")

                // The consequence of the refusal being INSIDE `harden` rather than beside
                // it, at the one place a caller reaches the hardener without naming it.
                testCase "scaffoldFSharp carries the refusal out rather than absorbing it" (fun _ ->
                    let v = VNode("n-1", "Note", [ "body", VUnion("Inline", [ "text", VStr "hello" ]) ])

                    match
                        Trust.scaffoldFSharp
                            noTrust
                            { refIdl with
                                Harden = HardenPolicy.Undeclared }
                            "wirehash-abc"
                            "agent:test"
                            v
                    with
                    | Error message ->
                        Expect.stringContains message "GatedKind" "the prose refusal names the undeclared member"
                    | Ok _ -> failtest "scaffolding an undeclared vocabulary must refuse, not emit") ]

          testList
              "the reader answer, INVERTED by Phase 180 — read D40 before changing it back"
              [ // Until Phase 180 `Artifact.readHarden` resolved an absent block through
                // `HardenPolicy.Default`, and this family asserted exactly that. The
                // assertion was not decoration: both published `idl.json` artifacts
                // carried no block, so that reader answer was what those bytes MEANT, and
                // emptying the default would have changed their meaning silently and with
                // a green build. D40 refused the flip on that measurement.
                //
                // **What changed is the world, not the argument.** Phase 179 made the
                // writer emit the block for every policy, so nothing this renderer
                // produces relies on the absent-block answer. `fuaran#1755` re-rendered
                // the two published artifacts — `fuaran-dotnet/src/Fuaran.UI.Idl/idl.json`
                // at `bb10065`, the shared cross-host corpus with it — and `roadmapctl
                // copies` reports both bundled host snapshots of that corpus in step. The
                // set of artifacts whose meaning the flip could change is empty, which is
                // the condition D40 named and the only thing that ever gated it.
                //
                // So an artifact with no block now reads as `Undeclared`: a vocabulary
                // that names no hardening tokens has not named them, and `Trust.harden`
                // refuses it by name rather than gating a tag no node carries. The
                // inversion is deliberate and this is the test that says so.
                testCase "an artifact with no block reads back as UNDECLARED, not as the retired default" (fun _ ->
                    let text = Artifact.render refIdl

                    Expect.stringContains text "\"harden\"" "since Phase 179 the projection emits the block always"

                    // What a pre-Phase-179 renderer wrote: the same artifact, block
                    // dropped at the JSON level rather than by text surgery.
                    let pre179 =
                        match Artifact.json refIdl with
                        | JObj members ->
                            Artifact.renderJson (JObj(members |> List.filter (fun (k, _) -> k <> "harden")))
                        | other -> failtestf "the artifact root is not a JSON object: %A" other

                    Expect.isFalse (pre179.Contains "\"harden\"") "the fixture is an artifact carrying no block"

                    match Artifact.parse pre179 with
                    | Ok back ->
                        Expect.equal
                            back.Harden
                            HardenPolicy.Undeclared
                            "an absent block reads back as Undeclared (Phase 180's inversion)"

                        Expect.notEqual
                            back.Harden
                            refIdl.Harden
                            "and emphatically not as the declaring vocabulary's own policy"
                    | Error e -> failtestf "the reference artifact did not parse: %s" e)

                // The consequence a CALLER meets, which is the half that makes the
                // inversion safe rather than merely different: the vocabulary such an
                // artifact decodes to does not harden. It refuses, naming the member.
                testCase "and the vocabulary it decodes to refuses to harden rather than gating nothing" (fun _ ->
                    let pre179 =
                        match Artifact.json refIdl with
                        | JObj members ->
                            Artifact.renderJson (JObj(members |> List.filter (fun (k, _) -> k <> "harden")))
                        | other -> failtestf "the artifact root is not a JSON object: %A" other

                    match Artifact.parse pre179 with
                    | Ok back ->
                        match Trust.checkHardenPolicy back noTrust with
                        | Error(CodegenError.UndeclaredHardenToken(named, _)) ->
                            Expect.equal named "GatedKind" "the first undeclared member in declaration order"
                        | Error other -> failtestf "refused, but with the wrong case: %A" other
                        | Ok() -> failtest "a blockless artifact must not decode to a vocabulary that hardens silently"
                    | Error e -> failtestf "the reference artifact did not parse: %s" e)

                // The other direction: a declared policy must survive the artifact round
                // trip AS DECLARED, or the inversion would be laundering every vocabulary
                // into `Undeclared` by a write-then-read.
                testCase "a declared policy round-trips as declared, distinctly from undeclared" (fun _ ->
                    Expect.notEqual
                        refIdl.Harden
                        HardenPolicy.Undeclared
                        "the fixture must actually declare its tokens, or this measures nothing"

                    match Artifact.parse (Artifact.render refIdl) with
                    | Ok back -> Expect.equal back.Harden refIdl.Harden "the declaration survives the round trip"
                    | Error e -> failtestf "the reference artifact did not parse: %s" e) ] ]
