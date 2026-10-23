module Fuaran.Core.Tests.IdlArtifactTests

open Expecto
open Fuaran.Core
open Fuaran.Core.Idl
open Fuaran.Core.Tests.ReferenceIdl
open Fuaran.Core.Idl.Codegen.Types

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
// and the workspace copy registry reports the corpus and both bundled host snapshots in step —
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
let private hardenRow (c: Classification) =
    match c.Change with
    | HardenPolicyChanged _ -> true
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
                // published artifacts, and the workspace copy registry reports both bundled host
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
                // classifier is what consumers read that claim through, so run it:
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
                        match Diff.ofArtifacts (a, None) (b, None) with
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

                    match Diff.ofArtifacts (before, None) (after, None) with
                    | Error m -> failtestf "the classifier refused the pair: %s" m
                    | Ok verdict ->
                        Expect.isNonEmpty
                            (verdict.Changes |> List.filter hardenRow)
                            "emptying every token is a hardening change and must be reported") ] ]

// ---------------------------------------------------------------------------
// Phase 252 — the artifact carries the vocabulary's own identity, and a hosted
// slot's declared wire form.
//
// `render` wrote one domain's description into every artifact, so an authored
// `idl.json` never re-rendered to itself. `renderWith` takes the identity and
// `identityOf` reads it back. A hosted slot's `wire` is a type object when declared
// and `"json"` when not — the latter byte-for-byte what every earlier artifact wrote.
//
// Phase 527 (D40 amended): `render` declares NO identity — no `name` and no
// `description` — where it used to write the UI vocabulary's prose into every
// artifact that declared none. A vocabulary that wants a description declares it.
// ---------------------------------------------------------------------------

let private hostedIdl (wire: IdlType option) (format: HostedFormat option) : Idl =
    { refIdl with
        Kinds =
            [ { Tag = "Trip"
                Category = "trip"
                Annotations = Annotations.Empty
                Fields =
                  [ { Name = "departs"
                      Opt = Required
                      Aliases = []
                      Presence = None
                      Annotations = Annotations.Empty
                      Type =
                        THosted
                            { Refusal = Fuaran.Core.Idl.HostedRefusal.Sentence
                              FSharp = "System.DateOnly"
                              Encode = "encDate"
                              Decode = "decDate"
                              Wire = wire
                              Format = format } } ] } ] }

[<Tests>]
let identityAndWireFormTests =
    testList
        "Phase 252 — identity and the hosted wire form in the artifact"
        [ testCase "an authored identity re-renders to itself" (fun _ ->
              let identity: Artifact.Identity =
                  { Name = Some "trips"
                    Description = Some "A packing list: trips hold bags, bags hold items." }

              let text = Artifact.renderWith identity refIdl

              Expect.stringContains text "\"name\": \"trips\"" "the name is written"
              Expect.stringContains text "\"description\": \"A packing list" "the description is written"

              Expect.equal
                  ((Artifact.codecWith identity).Write refIdl |> Result.map Artifact.renderJson)
                  (Ok text)
                  "renderWith is codecWith's write, laid out"

              match Artifact.identityOf text, Artifact.parse text with
              | Ok readBack, Ok idl ->
                  Expect.equal readBack identity "the identity reads back"
                  Expect.equal (Artifact.renderWith readBack idl) text "and the artifact re-renders byte-for-byte"
              | e1, e2 -> failtestf "did not read back: %A / %A" e1 e2)

          testCase "render declares no identity: no name and no description (D40 amended)" (fun _ ->
              let text = Artifact.render refIdl

              Expect.equal text (Artifact.renderWith Artifact.unnamed refIdl) "render is renderWith unnamed"

              match Json.parse text with
              | Ok(JObj members) ->
                  Expect.isFalse (members |> List.exists (fun (k, _) -> k = "name")) "no top-level name"

                  Expect.isFalse
                      (members |> List.exists (fun (k, _) -> k = "description"))
                      "and no description: a vocabulary that declares none gets none"
              | other -> failtestf "the artifact is not an object: %A" other

              Expect.equal
                  (Artifact.identityOf text)
                  (Ok Artifact.unnamed)
                  "an artifact without either member reads back as the unnamed identity"

              // The falsifier: a description alone is written alone, and read back.
              let described: Artifact.Identity =
                  { Name = None
                    Description = Some "described" }

              let withOne = Artifact.renderWith described refIdl
              Expect.notEqual withOne text "a declared description moves the bytes"
              Expect.equal (Artifact.identityOf withOne) (Ok described) "and reads back")

          testCase
              "a hosted slot's declared wire form and format round-trip, and an undeclared one is unchanged"
              (fun _ ->
                  for wire, format in [ None, None; Some TStr, Some HostedFormat.Date; Some(TList TStr), None ] do
                      let idl = hostedIdl wire format
                      let text = Artifact.render idl

                      match Artifact.parse text with
                      | Ok back ->
                          Expect.equal (Artifact.render back) text (sprintf "re-renders (%A, %A)" wire format)

                          Expect.equal
                              back.Kinds.Head.Fields.Head.Type
                              idl.Kinds.Head.Fields.Head.Type
                              "the slot reads back"
                      | Error e -> failtestf "did not parse: %s" e

                  Expect.stringContains
                      (Artifact.render (hostedIdl None None))
                      "\"wire\": \"json\""
                      "undeclared: as before"

                  Expect.stringContains
                      (Artifact.render (hostedIdl (Some TStr) (Some HostedFormat.Date)))
                      "\"format\": \"date\""
                      "the format") ]

// ---------------------------------------------------------------------------
// Phase 527 — the artifact is a codec.
//
// `idl.json` is written, read and described by one `Codec<Idl>` built from `Wire`'s
// combinators. What is pinned here: the omit rule is one table and every row of it holds in
// both directions; the reader is strict and COLLECTS; the schema is a real 2020-12 document
// that accepts every artifact the codec writes and refuses one it would not read; the
// committed schema is the codec's; and the shared corpus's `idl.json` re-renders to itself.
// The combinators the codec is the first in-repository consumer of are exercised beside it.
// ---------------------------------------------------------------------------

/// A vocabulary off every default the omit table names: an op, a flat wire shape, an annotated
/// field and kind, a wire-mapped enum with a case annotation.
let private offDefaults: Idl =
    let note =
        { Annotations.Empty with
            InProcessOnly = true
            Doc = Some "noted" }

    { refIdl with
        Kinds =
            [ { Tag = "Note"
                Category = "content"
                Annotations = note
                Fields =
                  [ { Name = "text"
                      Type = TStr
                      Opt = Required
                      Aliases = [ "body" ]
                      Presence = None
                      Annotations = note } ] } ]
        Unions = []
        Records = []
        Defaults = []
        Enums =
            [ { Declare.enumWith "Tone" [ "Loud", "loud"; "Quiet", "quiet" ] with
                  CaseAnnotations =
                      [ "Quiet",
                        { Annotations.Empty with
                            Since = Some "2" } ]
                  Aliases = [ "Quiet", [ "hush" ] ] } ]
        Ops =
            [ { Tag = "Touch"
                Category = "op"
                Annotations = Annotations.Empty
                Fields = [] } ]
        Wire =
            { WireShape.Default with
                NodeEnvelope = NodeEnvelopeShape.FlatKind } }
    // Phase 550 — a union away from the case-name tag rule, with a typed case-level host codec.
    |> fun idl ->
        { idl with
            Unions =
                [ { Name = "Mark"
                    Params = []
                    Annotations = Annotations.Empty
                    TagRule = UnionTagRule.LowerFirst
                    Cases =
                      [ { Tag = "Pinned"
                          Fields =
                            [ { Name = "at"
                                Type = TStr
                                Opt = Required
                                Aliases = []
                                Presence = None
                                Annotations = Annotations.Empty } ]
                          Annotations = Annotations.Empty
                          Hosted =
                            Some
                                { FSharp = "string"
                                  Encode = "fun s -> [ \"at\", JStr s ]"
                                  Decode = "Decoder.field \"at\" Decoder.str"
                                  Wire = None
                                  Format = None
                                  Refusal = HostedRefusal.Typed } } ]
                    Discrimination = UnionDiscrimination.Tagged } ] }
    // Phase 474 — a presence-keyed union whose second case synthesises the first's key, and an
    // inline record.
    |> fun idl ->
        let req (name: string) : IdlField =
            { Name = name
              Type = TStr
              Opt = Required
              Aliases = []
              Presence = None
              Annotations = Annotations.Empty }

        { idl with
            Unions =
                idl.Unions
                @ [ { Name = "Pick"
                      Params = []
                      Annotations = Annotations.Empty
                      TagRule = UnionTagRule.CaseName
                      Cases =
                        [ { Tag = "A"
                            Fields = [ req "a" ]
                            Annotations = Annotations.Empty
                            Hosted = None }
                          { Tag = "B"
                            Fields = [ req "b" ]
                            Annotations = Annotations.Empty
                            Hosted = None } ]
                      Discrimination =
                        UnionDiscrimination.ByPresence
                            [ { Case = "A"
                                Key = "a"
                                Synthesised = [] }
                              { Case = "B"
                                Key = "b"
                                Synthesised = [ ("a", VStr "") ] } ] } ]
            Records =
                [ { Name = "Grouped"
                    Fields = [ req "g" ]
                    Annotations = Annotations.Empty
                    Inline = true } ] }

/// The same vocabulary AT every default the omit table names.
let private atDefaults: Idl =
    { offDefaults with
        Kinds =
            offDefaults.Kinds
            |> List.map (fun k ->
                { k with
                    Annotations = Annotations.Empty
                    Fields =
                        k.Fields
                        |> List.map (fun f ->
                            { f with
                                Aliases = []
                                Annotations = Annotations.Empty }) })
        Enums = [ Declare.enumOf "Tone" [ "loud"; "quiet" ] ]
        Unions =
            offDefaults.Unions
            |> List.map (fun u ->
                { u with
                    TagRule = UnionTagRule.CaseName
                    Discrimination = UnionDiscrimination.Tagged
                    Cases = u.Cases |> List.map (fun c -> { c with Hosted = None }) })
        Records = offDefaults.Records |> List.map (fun r -> { r with Inline = false })
        Ops = []
        Wire = WireShape.Default }

/// Every member name anywhere in a JSON value.
let rec private memberNames (j: JVal) : string list =
    match j with
    | JObj ms -> ms |> List.collect (fun (k, v) -> k :: memberNames v)
    | JArr xs -> xs |> List.collect memberNames
    | _ -> []

let private schemaPath = "conformance/idl/idl.schema.json"

/// The committed schema file: the codec's schema with its stamp — the generator and the SHA-256 of
/// the unstamped rendering, so a hand edit is visible from the file alone (D164).
let private stampedSchema () : string =
    let body = Artifact.renderJson Artifact.schema

    let emission =
        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes body)
        |> System.Convert.ToHexString
        |> _.ToLowerInvariant()

    match Artifact.schema with
    | JObj members ->
        Artifact.renderJson (
            JObj(
                ("$comment",
                 JStr(
                     "GENERATED by Fuaran.Core.Idl - the JSON Schema of idl.json (Artifact.schema). "
                     + "Do not edit by hand: CORE_APPROVE_DOCS=idl-schema regenerates it. emission: sha256:"
                     + emission
                 ))
                :: members
            )
        )
    | other -> failtestf "the artifact schema is not an object: %A" other

let private build (schema: JVal) : Json.Schema.JsonSchema =
    Json.Schema.JsonSchema.FromText(
        Canonical.write Rendering.CrossHost schema,
        Json.Schema.BuildOptions(SchemaRegistry = Json.Schema.SchemaRegistry())
    )

let private accepts (schema: Json.Schema.JsonSchema) (text: string) : bool =
    use doc = System.Text.Json.JsonDocument.Parse text

    let options =
        Json.Schema.EvaluationOptions(OutputFormat = Json.Schema.OutputFormat.Flag)

    (schema.Evaluate(doc.RootElement, options)).IsValid

let private withRoot (f: (string * JVal) list -> (string * JVal) list) (idl: Idl) : JVal =
    match Artifact.json idl with
    | JObj ms -> JObj(f ms)
    | other -> failtestf "the artifact is not an object: %A" other

[<Tests>]
let codecTests =
    testList
        "Phase 527 — the artifact is a codec"
        [ testCase "every row of the omit table is omitted at its default and written away from it" (fun _ ->
              let at = memberNames (Artifact.json atDefaults) |> Set.ofList
              let off = memberNames (Artifact.json offDefaults) |> Set.ofList

              Expect.isNonEmpty Artifact.omitted "the table has rows"

              for owner, key in Artifact.omitted do
                  Expect.isFalse (at.Contains key) (sprintf "'%s' on %s is omitted at its default" key owner)
                  Expect.isTrue (off.Contains key) (sprintf "'%s' on %s is written away from its default" key owner)

              for v in [ atDefaults; offDefaults ] do
                  Expect.equal
                      (Artifact.parse (Artifact.render v))
                      (Ok(Artifact.canonicalise v))
                      "and each side reads back to its vocabulary")

          testCase "the reader is strict and collects every defect at its path" (fun _ ->
              let broken =
                  MiniIdl.miniIdl
                  |> withRoot (fun ms ->
                      (ms |> List.filter (fun (k, _) -> k <> "records" && k <> "defaults"))
                      @ [ "extra", JBool true ])

              match Artifact.ofJsonDetailed broken with
              | Ok _ -> failtest "a document missing two families and carrying an undeclared member read"
              | Error es ->
                  Expect.equal
                      (es |> List.map (fun e -> e.Code, e.Path))
                      [ DecodeCode.UndeclaredMember, [ PathSegment.Key "extra" ]
                        DecodeCode.MissingField, [ PathSegment.Key "records" ]
                        DecodeCode.MissingField, [ PathSegment.Key "defaults" ] ]
                      "the undeclared member first, then each declared member's defect, each at its path")

          testCase "a wrong encoding version is the one refusal, whatever else the document carries" (fun _ ->
              let future =
                  refIdl
                  |> withRoot (fun ms ->
                      (ms
                       |> List.map (fun (k, v) ->
                           if k = "version" then
                               k, JInt(Artifact.version + 1)
                           else
                               k, v))
                      @ [ "aMemberFromTheFuture", JBool true ])

              match Artifact.ofJsonDetailed future with
              | Error [ e ] ->
                  Expect.equal (e.Code, e.Path) (DecodeCode.OutOfRange, [ PathSegment.Key "version" ]) "at the version"
              | other -> failtestf "one refusal at the version: %A" other)

          testCase "a sentinel is read back only as itself" (fun _ ->
              let closureIdl =
                  { atDefaults with
                      Kinds =
                          [ { Tag = "Act"
                              Category = "action"
                              Annotations = Annotations.Empty
                              Fields =
                                [ { Name = "run"
                                    Type = TClosure
                                    Opt = Optional
                                    Aliases = []
                                    Presence = None
                                    Annotations = Annotations.Empty } ] } ] }

              let text = Artifact.render closureIdl
              Expect.stringContains text "\"wire\": \"<closure>\"" "the sentinel is written"

              match Artifact.parseDetailed (text.Replace("\"<closure>\"", "\"<redefined>\"")) with
              | Error [ e ] ->
                  Expect.equal e.Code DecodeCode.OutOfRange "a redefined sentinel is refused"
                  Expect.equal (List.last e.Path) (PathSegment.Key "wire") "at the sentinel"
              | other -> failtestf "one refusal at the sentinel: %A" other)

          testCase "an enum's mapping refusals sit at their own paths" (fun _ ->
              let enumAt (f: (string * JVal) list -> (string * JVal) list) =
                  offDefaults
                  |> withRoot (fun ms ->
                      ms
                      |> List.map (fun (k, v) ->
                          match k, v with
                          | "enums", JArr [ JObj e ] -> k, JArr [ JObj(f e) ]
                          | _ -> k, v))

              let shortHost =
                  enumAt (List.map (fun (k, v) -> if k = "hostCases" then k, JArr [ JStr "Loud" ] else k, v))

              match Artifact.ofJsonDetailed shortHost with
              | Error [ e ] ->
                  Expect.equal
                      (e.Code, e.Path)
                      (DecodeCode.OutOfRange,
                       [ PathSegment.Key "enums"; PathSegment.Index 0; PathSegment.Key "hostCases" ])
                      "a hostCases of the wrong length, at hostCases"
              | other -> failtestf "one refusal at hostCases: %A" other

              let unknownCase =
                  enumAt (
                      List.map (fun (k, v) ->
                          match k, v with
                          | "caseAnnotations", JObj [ (_, block) ] -> k, JObj [ "silent", block ]
                          | _ -> k, v)
                  )

              match Artifact.ofJsonDetailed unknownCase with
              | Error [ e ] ->
                  Expect.equal
                      (e.Code, e.Path)
                      (DecodeCode.UnknownTag,
                       [ PathSegment.Key "enums"
                         PathSegment.Index 0
                         PathSegment.Key "caseAnnotations"
                         PathSegment.Key "silent" ])
                      "an annotation on a case the enum does not declare, at its entry"
              | other -> failtestf "one refusal at the entry: %A" other)

          testCase "the schema is a 2020-12 document that accepts every artifact the codec writes" (fun _ ->
              let schema = build Artifact.schema

              for name, idl in
                  [ "reference", refIdl
                    "mini", MiniIdl.miniIdl
                    "at defaults", atDefaults
                    "off defaults", offDefaults ] do
                  Expect.isTrue (accepts schema (Artifact.render idl)) (sprintf "'%s' is accepted" name)

                  let identity: Artifact.Identity =
                      { Name = Some "n"
                        Description = Some "d" }

                  Expect.isTrue
                      (accepts schema (Artifact.renderWith identity idl))
                      (sprintf "'%s' with an identity is accepted" name)

              // The negative control: a validator that accepts everything certifies nothing.
              let broken = (Artifact.render refIdl).Replace("\"kinds\":", "\"kindz\":")
              Expect.isFalse (accepts schema broken) "a document the codec would not read is refused")

          testCase "the schema carries each recursive definition once, at the root" (fun _ ->
              match Artifact.schema with
              | JObj ms ->
                  Expect.equal
                      (ms |> List.tryFind (fun (k, _) -> k = "$schema") |> Option.map snd)
                      (Some(JStr "https://json-schema.org/draft/2020-12/schema"))
                      "the dialect is named"

                  match ms |> List.tryFind (fun (k, _) -> k = "$defs") with
                  | Some(_, JObj defs) ->
                      Expect.equal
                          (defs |> List.map fst |> List.sort)
                          [ "IdlType"; "IdlValue" ]
                          "the two recursive codecs"
                  | other -> failtestf "no root $defs: %A" other

                  Expect.isEmpty
                      (memberNames (JObj(ms |> List.filter (fun (k, _) -> k <> "$defs")))
                       |> List.filter ((=) "$defs"))
                      "and no definition is left below the root"
              | other -> failtestf "the schema is not an object: %A" other)

          testCase "the committed idl.schema.json is the codec's schema, stamped" (fun _ ->
              let path = Repo.file schemaPath
              let expected = stampedSchema ()
              Approval.write Approval.Docs Approval.Files.IdlSchema path expected |> ignore

              let committed =
                  if System.IO.File.Exists path then
                      System.IO.File.ReadAllText path
                  else
                      ""

              Expect.equal
                  committed
                  expected
                  (sprintf "%s is not the codec's schema - CORE_APPROVE_DOCS=idl-schema regenerates it" schemaPath))

          testCase "the shared corpus's idl.json re-renders to itself through the codec" (fun _ ->
              match SiblingCorpus.resolve ApplyVectorExport.familyDirName with
              | SiblingCorpus.NotAsked why -> skiptest why
              | SiblingCorpus.Absent why -> failtest why
              | SiblingCorpus.Found root ->
                  let text = System.IO.File.ReadAllText(System.IO.Path.Combine(root, "idl.json"))

                  match Artifact.parseDetailed text, Artifact.identityOf text with
                  | Ok idl, Ok identity ->
                      Expect.equal (Artifact.renderWith identity idl) text "byte for byte, under its own identity"
                      Expect.isTrue (accepts (build Artifact.schema) text) "and the schema accepts it"
                  | e1, e2 -> failtestf "the corpus artifact did not read: %A / %A" e1 e2) ]

// ---------------------------------------------------------------------------
// Phase 527 — the combinators `Codec` gained for `Artifact.codec`, its first in-repository
// production consumer (D122, D51).
// ---------------------------------------------------------------------------

type private Tree =
    | Leaf of int
    | Branch of Tree list

let private built (r: Result<Codec<'T>, CodecDeclarationFault list>) : Codec<'T> =
    match r with
    | Ok c -> c
    | Error fs -> failtestf "the declaration was refused: %A" fs

[<Tests>]
let combinatorTests =
    testList
        "Phase 527 — Codec.defaultField, entries, narrow, recursive and schemaDocument"
        [ testCase "defaultField omits at the default and reads the fallback when absent" (fun _ ->
              let c =
                  Codec.record id
                  |> Codec.defaultField "n" 0 ((=) 0) id Codec.int
                  |> Codec.build
                  |> built

              Expect.equal (c.Write 0) (Ok(JObj [])) "omitted at the default"
              Expect.equal (c.Write 3) (Ok(JObj [ "n", JInt 3 ])) "written away from it"
              Expect.equal (c.ReadAll(JObj [])) (Ok 0) "absent reads as the fallback"
              Expect.equal (c.ReadAll(JObj [ "n", JInt 0 ])) (Ok 0) "an explicit default reads"

              match c.Schema with
              | JObj ms ->
                  Expect.equal (ms |> List.tryFind (fst >> (=) "required")) (Some("required", JArr [])) "not required"
              | other -> failtestf "%A" other)

          testCase "entries reads every member under its own key and reports every defect" (fun _ ->
              let c = Codec.entries Codec.int
              let v = [ "b", 2; "a", 1 ]

              match c.Write v with
              | Ok j -> Expect.equal (c.ReadAll j) (Ok v) "round-trips in authored order"
              | Error f -> failtestf "%A" f

              match c.ReadAll(JObj [ "a", JStr "x"; "b", JInt 1; "c", JBool true ]) with
              | Error es ->
                  Expect.equal
                      (es |> List.map _.Path)
                      [ [ PathSegment.Key "a" ]; [ PathSegment.Key "c" ] ]
                      "each at its key"
              | Ok v -> failtestf "read %A" v)

          testCase "narrow raises each refusal by walking its path" (fun _ ->
              let positive =
                  Codec.entries Codec.int
                  |> Codec.narrow
                      (fun xs ->
                          match xs |> List.filter (fun (_, n) -> n < 0) with
                          | [] -> Ok xs
                          | bad ->
                              Error
                                  [ for k, n in bad ->
                                        [ PathSegment.Key k ], RootDecodeCode.OutOfRange, "a positive count", string n ])
                      id

              match positive.ReadAll(JObj [ "a", JInt -1; "b", JInt 2; "c", JInt -3 ]) with
              | Error es ->
                  Expect.equal
                      (es |> List.map (fun e -> e.Code, e.Path))
                      [ DecodeCode.OutOfRange, [ PathSegment.Key "a" ]
                        DecodeCode.OutOfRange, [ PathSegment.Key "c" ] ]
                      "every refusal, each where it says"
              | Ok v -> failtestf "read %A" v

              // A path the document does not hold is refused where the walk stops, never trusted.
              let lying =
                  Codec.int
                  |> Codec.narrow
                      (fun _ -> Error [ [ PathSegment.Key "nowhere" ], RootDecodeCode.OutOfRange, "x", "y" ])
                      id

              match lying.ReadAll(JInt 1) with
              | Error [ e ] -> Expect.equal (e.Code, e.Path) (DecodeCode.WrongKind, []) "the walk stops at the value"
              | other -> failtestf "%A" other)

          testCase "recursive declares a self-referential codec whose schema hoists to one definition" (fun _ ->
              let tree: Codec<Tree> =
                  Codec.recursive "Tree" (fun self ->
                      Codec.union
                          "$type"
                          [ Codec.case
                                "leaf"
                                (function
                                | Leaf n -> Some n
                                | _ -> None)
                                Leaf
                                (Codec.record id |> Codec.field "n" id Codec.int)
                            Codec.case
                                "branch"
                                (function
                                | Branch ts -> Some ts
                                | _ -> None)
                                Branch
                                (Codec.record id |> Codec.field "of" id (Codec.list self)) ]
                      |> built)

              let v = Branch [ Leaf 1; Branch [ Leaf 2 ] ]

              match tree.Write v with
              | Ok j -> Expect.equal (tree.ReadAll j) (Ok v) "round-trips through itself"
              | Error f -> failtestf "%A" f

              let pair =
                  Codec.record (fun a b -> a, b)
                  |> Codec.field "a" fst tree
                  |> Codec.field "b" snd tree
                  |> Codec.build
                  |> built

              let document = Codec.schemaDocument pair

              match document with
              | JObj ms ->
                  match ms |> List.tryFind (fun (k, _) -> k = "$defs") with
                  | Some(_, JObj defs) -> Expect.equal (List.map fst defs) [ "Tree" ] "one definition, used twice"
                  | other -> failtestf "no $defs: %A" other

                  Expect.isEmpty
                      (memberNames (JObj(ms |> List.filter (fun (k, _) -> k <> "$defs")))
                       |> List.filter ((=) "$defs"))
                      "none left below the root"
              | other -> failtestf "%A" other

              let schema = build document

              Expect.isTrue
                  (accepts
                      schema
                      """{"a":{"$type":"leaf","n":1},"b":{"$type":"branch","of":[{"$type":"leaf","n":2}]}}""")
                  "a valid pair"

              Expect.isFalse
                  (accepts
                      schema
                      """{"a":{"$type":"leaf","n":1},"b":{"$type":"branch","of":[{"$type":"leaf","n":"x"}]}}""")
                  "a nested defect is refused") ]

// ---------------------------------------------------------------------------
// Phase 526 — `support.json` read on the typed decode layer.
//
// `SupportArtifact.ofJson` was a nine-deep `Result.bind` over hand-rolled member readers whose
// refusals were sentences spelt in the file. It is a `Decoder` now, so every refusal carries a
// `DecodeCode` and the path to the value at fault, exactly as `idl.json`'s do — and these cases
// pin the CODE and the PATH, never the sentence, which is a projection and may be reworded.
// The writer did not move: the reference document renders to the bytes it rendered before.
// ---------------------------------------------------------------------------

/// The reference support document's rendered value, with `edit` applied to its root members.
let private supportWith (edit: (string * JVal) list -> (string * JVal) list) : JVal =
    match SupportArtifact.json ReferenceIdl.support with
    | JObj ms -> JObj(edit ms)
    | other -> failtestf "support.json renders an object, not %A" other

let private replaceMember (name: string) (value: JVal) (ms: (string * JVal) list) =
    ms |> List.map (fun (k, v) -> if k = name then k, value else k, v)

let private refusal (root: JVal) : DecodeCode * PathSegment list =
    match SupportArtifact.decoder root with
    | Ok _ -> failtestf "a defective support document was read: %A" root
    | Error e -> e.Code, e.Path

[<Tests>]
let supportReaderTests =
    testList
        "support.json is read on the typed decode layer (Phase 526)"
        [ testCase "the reference document round-trips through the decoder, byte for byte" (fun _ ->
              let text = SupportArtifact.render ReferenceIdl.support

              match SupportArtifact.parseDetailed text with
              | Error e -> failtestf "the reference support document did not read: %s" (DecodeError.render e)
              | Ok doc ->
                  Expect.equal doc ReferenceIdl.support "the same document back"
                  Expect.equal (SupportArtifact.render doc) text "and the same bytes"

              Expect.isOk (SupportArtifact.parse (SupportArtifact.render SupportDocument.Empty)) "the empty document")

          testCase "a refusal names its code and the path to the value at fault" (fun _ ->
              let key = PathSegment.Key
              let idx = PathSegment.Index

              let firstEntry (name: string) (edit: (string * JVal) list -> (string * JVal) list) =
                  supportWith (fun ms ->
                      ms
                      |> List.map (fun (k, v) ->
                          match k, v with
                          | k, JArr(JObj first :: rest) when k = name -> k, JArr(JObj(edit first) :: rest)
                          | _ -> k, v))

              let without (name: string) (ms: (string * JVal) list) =
                  ms |> List.filter (fun (k, _) -> k <> name)

              let cases =
                  [ "no version", supportWith (without "version"), (DecodeCode.MissingField, [ key "version" ])
                    "a version that is not an integer",
                    supportWith (replaceMember "version" (JStr "1")),
                    (DecodeCode.WrongKind, [ key "version" ])
                    "another encoding version",
                    supportWith (replaceMember "version" (JInt(SupportArtifact.version + 1))),
                    (DecodeCode.OutOfRange, [ key "version" ])
                    "a root that is not an object", JArr [], (DecodeCode.WrongKind, [])
                    "docs that are not an array",
                    supportWith (replaceMember "docs" (JStr "x")),
                    (DecodeCode.WrongKind, [ key "docs" ])
                    "a doc line that is not a string",
                    firstEntry "docs" (replaceMember "lines" (JArr [ JInt 1 ])),
                    (DecodeCode.WrongKind, [ key "docs"; idx 0; key "lines"; idx 0 ])
                    "a refine without its expression",
                    firstEntry "caseRefines" (without "expression"),
                    (DecodeCode.MissingField, [ key "caseRefines"; idx 0; key "expression" ])
                    "a projection without its decoder",
                    firstEntry "kindProjections" (without "decoder"),
                    (DecodeCode.MissingField, [ key "kindProjections"; idx 0; key "decoder" ])
                    "a projection's record fields that are not an array",
                    firstEntry "kindProjections" (fun ms -> ms @ [ "recordFields", JInt 3 ]),
                    (DecodeCode.WrongKind, [ key "kindProjections"; idx 0; key "recordFields" ])
                    "a splices block that is not an object — it used to read as no splices at all",
                    supportWith (fun ms -> without "splices" ms @ [ "splices", JArr [] ]),
                    (DecodeCode.WrongKind, [ key "splices" ])
                    "a prelude without its path",
                    supportWith (replaceMember "hostPrelude" (JObj [ "module", JStr "Prelude" ])),
                    (DecodeCode.MissingField, [ key "hostPrelude"; key "path" ]) ]

              for name, root, expected in cases do
                  Expect.equal (refusal root) expected name)

          testCase "the version is refused FIRST and alone, before any member it might explain" (fun _ ->
              let root =
                  supportWith (fun ms ->
                      replaceMember "version" (JInt(SupportArtifact.version + 1)) ms
                      @ [ "splices", JStr "a future shape" ])

              Expect.equal
                  (refusal root)
                  (DecodeCode.OutOfRange, [ PathSegment.Key "version" ])
                  "the version, not the splices")

          testCase
              "the string forms render the refusal with its code and path; text that is not JSON is InvalidJson"
              (fun _ ->
                  match SupportArtifact.parse "{\"version\": \"1\"}" with
                  | Ok _ -> failtest "a string version was read"
                  | Error e -> Expect.stringStarts e "WrongKind at $[\"version\"]: " "code, path, sentence"

                  match SupportArtifact.ofJson (JObj [ "version", JStr "1" ]) with
                  | Ok _ -> failtest "a string version was read from the root"
                  | Error e ->
                      Expect.stringStarts e "WrongKind at $[\"version\"]: " "the same line from the parsed root"

                  match SupportArtifact.parseDetailed "{" with
                  | Ok _ -> failtest "text that is not JSON was read"
                  | Error e -> Expect.equal (e.Code, e.Path) (DecodeCode.InvalidJson, []) "refused at the root")

          testCase "a member the document does not know is tolerated, as it always was" (fun _ ->
              let root = supportWith (fun ms -> ms @ [ "x-future", JBool true ])

              match SupportArtifact.decoder root with
              | Ok doc -> Expect.equal doc ReferenceIdl.support "the known members read as before"
              | Error e -> failtestf "an unknown member was refused: %s" (DecodeError.render e)) ]
