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
// `DECISIONS.md` D40 carries the list. The refusal therefore ships REACHABLE and
// OPT-IN: `HardenPolicy.Undeclared` beside `Default`, `Trust.checkHardenPolicy` and
// `Trust.hardenOrRefuse` beside `harden`, and nothing in the existing path changed.
//
// Two families below, and the second is the one that matters years from now. The first
// pins the refusal per member, each case going red on its own. The second pins the
// COMPAT PROMISE the flip would have broken — an artifact with no `harden` block reads
// back as `Default` — so a later session that flips the default anyway meets a red test
// naming the decision rather than a silent change to what published bytes mean.
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

                // The measurement's direct consequence, as an assertion: `Default`
                // declares every member, so every consumer on it — which D40 measured
                // as all of them — is untouched by this phase.
                testCase "HardenPolicy.Default never refuses" (fun _ ->
                    let onDefault =
                        { refIdl with
                            Harden = HardenPolicy.Default }

                    Expect.isOk (Trust.checkHardenPolicy onDefault noTrust) "the default declares every member"
                    Expect.isOk (Trust.checkHardenPolicy onDefault withUrlField) "including the URL pair")

                // An empty `TransparentUnions` is a DECLARATION, not an absence — it is
                // how a vocabulary no case of which encodes bare says so, and `refIdl`
                // says exactly that on purpose. Refusing it would make that vocabulary
                // unexpressible.
                testCase "an empty TransparentUnions is a declaration, not an omission" (fun _ ->
                    Expect.isEmpty refIdl.Harden.TransparentUnions "the fixture declares no transparent union"
                    Expect.isOk (Trust.checkHardenPolicy refIdl noTrust) "and that is not a refusal")

                testCase "hardenOrRefuse returns exactly what harden returns when declared" (fun _ ->
                    let v = VNode("n-1", "Note", [ "body", VUnion("Inline", [ "text", VStr "hello" ]) ])

                    Expect.equal
                        (Trust.hardenOrRefuse refIdl noTrust v)
                        (Ok(Trust.harden refIdl noTrust v))
                        "the opt-in entry point changes nothing for a declared vocabulary")

                testCase "hardenOrRefuse refuses an undeclared vocabulary before hardening" (fun _ ->
                    let v = VNode("n-1", "Note", [ "body", VUnion("Inline", [ "text", VStr "hello" ]) ])

                    match
                        Trust.hardenOrRefuse
                            { refIdl with
                                Harden = HardenPolicy.Undeclared }
                            noTrust
                            v
                    with
                    | Error(CodegenError.UndeclaredHardenToken _) -> ()
                    | Error other -> failtestf "refused, but with the wrong case: %A" other
                    | Ok _ -> failtest "an undeclared policy must refuse rather than harden through") ]

          testList
              "the compat promise D40 measured — this is the guard, read D40 before changing it"
              [ // `Artifact.readHarden` resolves an absent block through `Default`. Both
                // published `idl.json` artifacts in the estate — including the shared
                // cross-host corpus — carry no block, so that reader answer is what those
                // bytes MEAN. Emptying or removing `Default` changes their meaning
                // silently; this test is what stops that being silent.
                //
                // **Amended by Phase 179, and only the writer half moved.** That phase
                // made `Artifact.render` emit the block UNCONDITIONALLY — step one of the
                // two-step migration D40 laid out — so the old assertion here ("the
                // projection omits the block at the default") is now false by design and
                // is replaced by its successor: the block is present on a fresh render,
                // and an artifact written BEFORE that phase still reads back as `Default`.
                // The promise this family guards is unchanged; what changed is that the
                // artifact carrying no block is now a historical one rather than one this
                // renderer still produces. Retiring the reader answer is Phase 180.
                testCase "an artifact with no block still reads back as the default" (fun _ ->
                    let onDefault =
                        { refIdl with
                            Harden = HardenPolicy.Default }

                    let text = Artifact.render onDefault

                    Expect.stringContains
                        text
                        "\"harden\""
                        "since Phase 179 the projection emits the block at the default too"

                    // What a pre-Phase-179 renderer wrote: the same artifact, block
                    // dropped at the JSON level rather than by text surgery.
                    let pre179 =
                        match Artifact.json onDefault with
                        | JObj members ->
                            Artifact.renderJson (JObj(members |> List.filter (fun (k, _) -> k <> "harden")))
                        | other -> failtestf "the artifact root is not a JSON object: %A" other

                    Expect.isFalse (pre179.Contains "\"harden\"") "the fixture is an artifact carrying no block"

                    match Artifact.parse pre179 with
                    | Ok back ->
                        Expect.equal
                            back.Harden
                            HardenPolicy.Default
                            "an absent block reads back as Default — NOT as Undeclared"

                        Expect.notEqual
                            back.Harden
                            HardenPolicy.Undeclared
                            "if this ever fails, every published artifact's hardening has changed meaning"
                    | Error e -> failtestf "the reference artifact did not parse: %s" e)

                // The other direction: an opted-in vocabulary must survive the artifact
                // round trip AS undeclared, or the opt-in would be laundered back into
                // the default by a write-then-read.
                testCase "an undeclared policy round-trips as undeclared, distinctly from the default" (fun _ ->
                    let undeclared =
                        { refIdl with
                            Harden = HardenPolicy.Undeclared }

                    Expect.notEqual
                        HardenPolicy.Undeclared
                        HardenPolicy.Default
                        "the two are different claims and must not compare equal"

                    match Artifact.parse (Artifact.render undeclared) with
                    | Ok back -> Expect.equal back.Harden HardenPolicy.Undeclared "the opt-in survives the round trip"
                    | Error e -> failtestf "the undeclared artifact did not parse: %s" e) ] ]
