module Fuaran.Core.Tests.ProofOracle.WireVersioningTests

// The `Proofs.Oracle` differential for `proofs/WireVersioning.fst`: the extracted model
// (`proofs/oracle/WireVersioning.fs`) run beside production.
// The bridges more than one model reads are in `ProofOracle.Bridges.fs`.

open System.IO
open Expecto

open Fuaran.Core
open Fuaran.Core.Tests
open Fuaran.Core.Tests.Reference
open Fuaran.Core.Tests.ProofOracle.Bridges
open Fuaran.Core.Tests.CanonOracle

// ---------------------------------------------------------------------------
//  Phase 151 — the EVOLUTION POLICY: `WireVersioning` beside `Versioning`.
// ---------------------------------------------------------------------------
//
// `proofs/WireVersioning.fst` models WIRE_FORMAT §15.4's table — `Versioning.classify` / `bump` /
// `negotiate` / `decodeTolerant` / `reencode` — and proves the four soundness statements the
// table's prose asserts. This section runs the extraction beside production over the two inputs
// the shard names: the corpus's `envelope/` family for the DECODE half, and pairs of `idl.json`
// revisions for the CLASSIFY half.
//
// The model's tag type is `WireCanon.ch list`, which is why the bridges above are reused rather
// than a second set written: a tag is a string read out of a `jval`, and Phase 149 already has
// the string bridge that matches the model's own character alphabet.

/// A production `Versioning.Capability` as the model reads it. The two counters cross a width
/// boundary — production holds `int`, the model's `nat` extracts to `BigInteger` per
/// `oracle/Prims.fs` — so the conversion is here and not hidden inside a comparison.
let private toModelProfile (p: Versioning.Capability) : WireVersioning.profile =
    { WireVersioning.name = canonToChs p.Name
      WireVersioning.major = bigint p.Major
      WireVersioning.minor = bigint p.Minor }

/// A vocabulary — production's `Set<string>` as the model's list of tags. `Set.toList` hands back
/// a sorted duplicate-free list, which is the only shape `classify`'s list-valued result can be
/// compared against element for element rather than as a set.
let private toModelVocab (v: Set<string>) : WireCanon.ch list list = v |> Set.toList |> List.map canonToChs

/// The model's `evolution` rendered as production's `Versioning.Evolution`, so the comparison is
/// one equality over the shipped type rather than a pair of shape tests.
let private ofModelEvolution (e: WireVersioning.evolution) : Versioning.Evolution =
    match e with
    | WireVersioning.Additive added -> Versioning.Evolution.Additive(added |> List.map canonFromChs)
    | WireVersioning.Breaking(removed, added) ->
        Versioning.Evolution.Breaking(removed |> List.map canonFromChs, added |> List.map canonFromChs)

/// The classification's render pair: production's verdict, and the model's read back as
/// production's type, both through `%A`.
let private evolution: Differential<Versioning.Evolution, WireVersioning.evolution> =
    Differential.pair (sprintf "%A") (ofModelEvolution >> sprintf "%A")

/// One classification, asked of production and of the model's `classify`. The comparison is the
/// WHOLE verdict — both lists, not merely the `Additive`/`Breaking` discriminator — because a
/// classifier that named the right class and the wrong tags would leave a migration author with the
/// wrong shim.
///
/// `classify` is the model's `classify` in every real run. The GO-RED instrument, and this family's
/// counterpart to the canon family's reversed comparator, is the model's OWN
/// `classify_ignoring_removals`, a classifier that computes the additions and never looks for
/// removals. It is not a strawman — it is what an author writes who reads §15.4's additive row and
/// stops there, and it agrees with production on every purely additive change. A comparison that
/// cannot separate it from the real thing is measuring nothing.
let private versionProbe
    (classify: WireCanon.ch list list -> WireCanon.ch list list -> WireVersioning.evolution)
    (label: string, before: Set<string>, after: Set<string>)
    (t: Tally)
    : Tally =
    t
    |> Differential.compareWith
        evolution
        (fun p m -> sprintf "%s: production classified\n  %s\nthe model classified\n  %s" label p m)
        (Versioning.classify before after)
        (classify (toModelVocab before) (toModelVocab after))

// ---- the IDL pairs ----------------------------------------------------------------------
//
// The pairs are produced by PERTURBING the pinned `idl.json` and re-reading it through
// `Diff.snapshot`, rather than by writing two tag sets by hand. That matters: the claim the row
// carries is about the classifier applied to an IDL DIFF, and a hand-written pair would prove the
// classifier agrees with itself over two lists somebody chose. Going through the artifact reader
// exercises the path a migration author actually walks.

/// The pinned corpus's `idl.json`, parsed. Resolved through the same sibling-corpus seam every
/// other corpus-reading family here uses.
let private pinnedIdlText () =
    match SiblingCorpus.resolve "nodes" with
    | SiblingCorpus.Found root -> System.IO.File.ReadAllText(System.IO.Path.Combine(root, "idl.json"))
    | SiblingCorpus.NotAsked why -> skiptest why
    | SiblingCorpus.Absent why -> failtest why

/// The kind tags an `idl.json` text declares, read through the production artifact differ rather
/// than by picking the JSON apart here.
let private kindTagsOf (label: string) (text: string) : Set<string> =
    match Fuaran.Core.Idl.Diff.parse text None with
    | Result.Error m -> failtestf "the %s idl.json did not snapshot: %s" label m
    | Result.Ok(snap: Fuaran.Core.Idl.Codegen.Types.Snapshot) -> snap.Kinds |> Map.toList |> List.map fst |> Set.ofList

/// Rewrite the `kinds` array of an `idl.json` JVal. Every perturbation below is one of these.
let private mapKinds (f: JVal list -> JVal list) (idl: JVal) : JVal =
    match idl with
    | JObj fields ->
        JObj(
            fields
            |> List.map (fun (k, v) ->
                match k, v with
                | "kinds", JArr xs -> k, JArr(f xs)
                | _ -> k, v)
        )
    | other -> other

let private kindTag (k: JVal) : string =
    match k with
    | JObj fields ->
        match fields |> List.tryPick (fun (n, v) -> if n = "tag" then Some v else None) with
        | Some(JStr s) -> s
        | _ -> failtest "a kind in idl.json carries no string `tag`"
    | _ -> failtest "a kind in idl.json is not an object"

let private withTag (tag: string) (k: JVal) : JVal =
    match k with
    | JObj fields -> JObj(fields |> List.map (fun (n, v) -> if n = "tag" then n, JStr tag else n, v))
    | other -> other

/// Give a kind one more field, at a named optionality class. §15.4's field rows are all this
/// perturbation with a different class in the one slot, and the perturbation is deliberately one
/// that changes NO tag: that is what makes it invisible to the kind-tag delta (the model's
/// section 6) and visible to the composition that decides it (section 7).
let private withAnExtraField (optClass: string) (k: JVal) : JVal =
    match k with
    | JObj fields ->
        let extra =
            JObj
                [ "name", JStr "phase151ProbeField"
                  "optionality", JObj [ "$type", JStr optClass ]
                  "type", JObj [ "$type", JStr "prim"; "name", JStr "String" ] ]

        JObj(
            fields
            |> List.map (fun (n, v) ->
                match n, v with
                | "fields", JArr xs -> n, JArr(xs @ [ extra ])
                | _ -> n, v)
        )
    | other -> other

/// The four perturbations §15.4's table distinguishes, each as `(label, before-text, after-text)`.
/// `Canonical.write Rendering.CrossHost` is what writes them back, so the perturbed artifact is canonical bytes and the
/// snapshot reader meets exactly what it would meet in a repository.
let private idlPairs () : (string * Set<string> * Set<string>) list =
    let text = pinnedIdlText ()

    let idl =
        match Json.parse text with
        | Result.Error m -> failtestf "the pinned idl.json did not parse: %s" m
        | Result.Ok v -> v

    let before = kindTagsOf "pinned" text
    let firstTag = before |> Set.toList |> List.head

    let render (v: JVal) = Canonical.write Rendering.CrossHost v

    let added =
        idl
        |> mapKinds (fun xs ->
            match xs with
            | first :: _ -> xs @ [ withTag "Phase151ProbeKind" first ]
            | [] -> xs)
        |> render

    let optionalField =
        idl
        |> mapKinds (
            List.map (fun k ->
                if kindTag k = firstTag then
                    withAnExtraField "optional" k
                else
                    k)
        )
        |> render

    let removed =
        idl |> mapKinds (List.filter (fun k -> kindTag k <> firstTag)) |> render

    let renamed =
        idl
        |> mapKinds (
            List.map (fun k ->
                if kindTag k = firstTag then
                    withTag (firstTag + "Renamed") k
                else
                    k)
        )
        |> render

    [ "add a kind", before, kindTagsOf "kind-added" added
      "add an optional field", before, kindTagsOf "optional-field-added" optionalField
      "remove a tag", before, kindTagsOf "tag-removed" removed
      "rename", before, kindTagsOf "renamed" renamed ]

// ---- §15.4's FIELD rows, at the composition that decides them (Phase 200) ----------------
//
// The pairs above are the classify half AT THE KIND-TAG DELTA, and at that delta a field addition
// is invisible: the "add an optional field" pair classifies `Additive []`, the no-op arm. Until
// Phase 200 that was recorded as §15.4's optional-field row being satisfied VACUOUSLY, and
// permanently so, on the argument that widening `classify` to see fields would model a function
// this repository does not ship.
//
// The argument was wrong in one specific, checkable way. `Versioning.classify` does not take kind
// tags — it takes two SUBJECT SETS — and the shipped caller that builds them from an IDL diff is
// `Diff.evolution`, which partitions each row by the SEVERITY `Diff.classifyFieldAdd` gives it
// from the field's optionality class. `Diff.bumpProfile` then carries the verdict through
// `Versioning.bump`. The row is decided by shipped code all the way to a published profile, on a
// path this family did not walk. Walking it is this section, and the model's section 7 is the
// same composition proved.
//
// Three perturbations, one per class the classifier branches on, each adding ONE field to the
// pinned artifact and changing no tag:
//
//   `optional` — §15.4's row itself. A subject is INTRODUCED, the verdict is a NON-EMPTY
//                `Additive`, the MINOR moves, and an old consumer is `Behind` rather than blind.
//   `required` — RETIRED, since Phase 304: every document written under the old contract lacks the
//                member and the decoder refuses it, so the MAJOR moves and an old consumer is
//                `Foreign`. Until that phase this arm rode the minor on the claim that every
//                existing document decodes, which the model's section 8 and the evolution
//                differential in `IdlStabilityClassTests.fs` both refute.
//   `hostOnly` — WIRE_FORMAT §9's wire-omitted fields: on no document in either direction, so no
//                profile moves. This is the arm that makes the other two a MEASUREMENT rather
//                than a constant, and it is the one the go-red gets wrong.

module IdlDiff = Fuaran.Core.Idl.Diff

/// The model's `severity` as production's, so the comparison is one equality over the shipped type
/// rather than a pair of shape tests.
let private ofModelSeverity (s: WireVersioning.severity) : Fuaran.Core.Idl.Codegen.Types.Severity =
    match s with
    | WireVersioning.SAdditive -> Fuaran.Core.Idl.Codegen.Types.Additive
    | WireVersioning.SBreakingForEmitters -> Fuaran.Core.Idl.Codegen.Types.BreakingForEmitters
    | WireVersioning.SBreakingWire -> Fuaran.Core.Idl.Codegen.Types.BreakingWire
    | WireVersioning.SHostSurfaceOnly -> Fuaran.Core.Idl.Codegen.Types.HostSurfaceOnly
    | WireVersioning.SUnclassifiable -> Fuaran.Core.Idl.Codegen.Types.Unclassifiable

/// A model profile as the three components a publisher reads. The counters cross a width boundary
/// in the other direction from `toModelProfile` — the model's `nat` extracts to `BigInteger` — so
/// the conversion is here and not hidden inside a comparison.
let private profileTriple (p: WireVersioning.profile) : string * int * int =
    canonFromChs p.name, int p.major, int p.minor

/// The SUBJECT string production renders for one classification row. `Diff.summarise` is private,
/// so it is recovered through the public `Diff.evolution` over the single row — which hands the
/// string back WITHOUT presupposing which partition it lands in, since either list answers. That
/// matters: the partition is exactly what this family compares, so it must not be an input to the
/// comparison. A row that moves neither set contributes to neither, and its subject is read by
/// nothing.
let private subjectOf (c: Fuaran.Core.Idl.Codegen.Types.Classification) : string =
    match IdlDiff.evolution [ c ] with
    | Versioning.Evolution.Additive(s :: _) -> s
    | Versioning.Evolution.Breaking(s :: _, _) -> s
    | Versioning.Evolution.Breaking([], s :: _) -> s
    | _ -> "<this row moves neither subject set>"

/// What one field-add perturbation measures on both sides.
type private FieldAddMeasurement =
    {
        /// The optionality class the perturbation declared, read back out of the artifact.
        OptClass: string
        ProductionSeverity: Fuaran.Core.Idl.Codegen.Types.Severity
        ModelSeverity: Fuaran.Core.Idl.Codegen.Types.Severity
        /// The profile `core@1.0` bumps to, as `Diff.bumpProfile` computes it.
        ProductionProfile: string * int * int
        /// The same, computed end to end by the model: its own `classify_field_add`, its own
        /// `evolution_of`, its own `bump`.
        ModelProfile: string * int * int
        /// And by the model's GO-RED — a classifier that answers additive whatever the class says.
        GoRedProfile: string * int * int
        BreaksEmitters: bool
    }

let private fieldAddProbe (optClass: string) (beforeText: string) (afterText: string) : FieldAddMeasurement =
    match IdlDiff.ofArtifacts (beforeText, None) (afterText, None) with
    | Result.Error m -> failtestf "the `%s` field-add perturbation did not classify: %s" optClass m
    | Result.Ok v ->
        let row =
            match v.Changes with
            | [ one ] -> one
            | rows ->
                failtestf
                    "the `%s` field-add perturbation produced %d rows, not the single FieldAdded it adds — the perturbation moved something else: %A"
                    optClass
                    (List.length rows)
                    (rows |> List.map _.Change)

        let declared =
            match row.Change with
            | Fuaran.Core.Idl.Codegen.Types.FieldAdded(_, f) -> f.Presence.Class
            | other -> failtestf "the `%s` perturbation's only row is %A, not a FieldAdded" optClass other

        let subject = canonToChs (subjectOf row)
        let baseProfile = toModelProfile Versioning.Capability.coreV1

        let modelSeverity = WireVersioning.classify_field_add (canonToChs declared)

        let goRedSeverity =
            WireVersioning.classify_field_add_ignoring_optionality (canonToChs declared)

        let modelBump (sev: WireVersioning.severity) =
            profileTriple (WireVersioning.bump baseProfile (WireVersioning.evolution_of [ sev, subject ]))

        let productionProfile =
            match IdlDiff.bumpProfile Versioning.Capability.coreV1 v with
            | Fuaran.Core.Idl.Codegen.Types.Bump.Bumped p -> p.Name, p.Major, p.Minor
            | Fuaran.Core.Idl.Codegen.Types.Bump.Undecided rows ->
                failtestf "the `%s` field-add perturbation left %d rows undecided" optClass (List.length rows)

        { OptClass = declared
          ProductionSeverity = row.Severity
          ModelSeverity = ofModelSeverity modelSeverity
          ProductionProfile = productionProfile
          ModelProfile = modelBump modelSeverity
          GoRedProfile = modelBump goRedSeverity
          BreaksEmitters = v.BreaksEmitters }

/// The three perturbations, each derived from the PINNED artifact and read back through
/// `Diff.parse`, for the reason `idlPairs` gives: the claim is about the classifier applied to an
/// IDL diff, and a hand-written pair would establish that it agrees with itself.
let private fieldAddMeasurements () : FieldAddMeasurement list =
    let text = pinnedIdlText ()

    let idl =
        match Json.parse text with
        | Result.Error m -> failtestf "the pinned idl.json did not parse: %s" m
        | Result.Ok v -> v

    let firstTag = kindTagsOf "pinned" text |> Set.toList |> List.head
    let before = Canonical.write Rendering.CrossHost idl

    let perturb (optClass: string) =
        idl
        |> mapKinds (
            List.map (fun k ->
                if kindTag k = firstTag then
                    withAnExtraField optClass k
                else
                    k)
        )
        |> Canonical.write Rendering.CrossHost

    [ "optional"; "required"; "hostOnly" ]
    |> List.map (fun c -> fieldAddProbe c before (perturb c))

// ---- the envelope family ----------------------------------------------------------------
//
// The corpus's six `envelope/` fixtures are the DECODE half. Each carries a `$profile` and a
// `$payload`; the consumer is `core@1.0` and its vocabulary is the one the fixtures were built
// against — `Markdown` known, `hologram` not. The discriminator is the payload's `kind.$type`,
// and `requiredProfile` sits beside `kind` on the payload object, which is exactly where
// production's own `readRequiredProfile` looks.

/// The consumer's vocabulary for the envelope family, as the fixtures declare it by construction.
let private envelopeVocab: Set<string> = Set.ofList [ "Markdown" ]

let private envelopeTagOf (el: JVal) : Result<string, string> =
    Decoder.describing (Decoder.field "kind" Decoder.json) el
    |> Result.bind (Decoder.describing (Decoder.field "$type" Decoder.json))
    |> Result.bind (Decoder.describing Decoder.str)

let private envelopeRequiredProfile (el: JVal) : Versioning.Capability option =
    match Decoder.describing (Decoder.field "requiredProfile" Decoder.json) el with
    | Result.Ok(JStr s) ->
        match Versioning.Capability.tryParse s with
        | Result.Ok p -> Some p
        | Result.Error _ -> None
    | _ -> None

/// The model's counterparts, over the bridged value. `decode_known` is the identity on the parsed
/// object on BOTH sides: this family is about the tolerance boundary, not about a domain codec,
/// and handing the two sides different known-decoders would compare the decoders instead.
let private envelopeTagOfModel (el: WireCanon.jval<int, float>) : WireCanon.outcome<WireCanon.ch list> =
    match envelopeTagOf (canonOfModel el) with
    | Result.Ok s -> WireCanon.Ok(canonToChs s)
    | Result.Error m -> WireCanon.Error m

let private envelopeRequiredProfileModel (el: WireCanon.jval<int, float>) =
    match envelopeRequiredProfile (canonOfModel el) with
    | Some p -> FStar_Pervasives_Native.Some(toModelProfile p)
    | None -> FStar_Pervasives_Native.None

/// The corpus's declared `envelope-reject` rows, keyed by fixture (`envelope/<name>`, as the probe
/// labels it) to the code and path each is answered with — read from the corpus `manifest.json`,
/// the authority for what a fixture expects.
let private envelopeRejects () : Map<string, string * string> =
    match SiblingCorpus.resolve "envelope" with
    | SiblingCorpus.Found root ->
        use doc =
            System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "manifest.json")))

        let str (e: System.Text.Json.JsonElement) (name: string) =
            match e.TryGetProperty name with
            | true, v when v.ValueKind = System.Text.Json.JsonValueKind.String -> v.GetString()
            | _ -> ""

        [ for e in doc.RootElement.GetProperty("fixtures").EnumerateArray() do
              if str e "kind" = "envelope-reject" then
                  let input = str e "inputFile"

                  yield
                      Path.ChangeExtension(input, null).Replace('\\', '/'),
                      (str e "expectedErrorCode", str e "expectedPath") ]
        |> Map.ofList
    | _ -> Map.empty

/// One envelope fixture, asked of production and of the model. Four things are compared, and the
/// last is the one §15.3 is about:
///   1. the NEGOTIATION — `Current` / `Behind` / `Foreign`, and the authored profile each carries;
///   2. whether the tolerant decode found a KNOWN kind or preserved an UNKNOWN one;
///   3. the `requiredProfile` the unknown carries, which is what a degraded placeholder names;
///   4. the BYTES out of `reencode` + `Canonical.write Rendering.CrossHost` — which for a `Foreign` fixture is not asked,
///      because a `Foreign` artifact is refused before any payload is decoded.
///
/// A fixture production REFUSES is compared on the refusal alone: the corpus must declare it an
/// `envelope-reject` answered `FOREIGN_PROFILE` at `$.$profile` (WIRE_FORMAT §15.2, Phase 1938: a
/// `$profile` that does not parse names no capability set), and production's refusal must be the
/// profile's. The model negotiates parsed profiles only, so there is nothing further to ask it.
/// Any other refusal is a defect, as before.
let private envelopeProbe (rejects: Map<string, string * string>) (label: string) (text: string) (t: Tally) : Tally =
    match Versioning.parse text with
    | Result.Error m ->
        match Map.tryFind label rejects with
        // Phase 520: production's refusal is TYPED, so the arm asserts the code and the path the
        // corpus declares, never a sentence's prefix.
        | Some("FOREIGN_PROFILE", "$.$profile") when
            (match m with
             | Versioning.EnvelopeRefusal.ForeignProfile _ ->
                 Versioning.EnvelopeRefusal.path m = [ PathSegment.Key "$profile" ]
             | Versioning.EnvelopeRefusal.Malformed _ -> false)
            ->
            t
        | declared ->
            failtestf
                "the envelope fixture %s did not parse: %s (the corpus declares it %s)"
                label
                (Versioning.EnvelopeRefusal.describe m)
                (match declared with
                 | Some(code, path) -> sprintf "a reject, %s at %s" code path
                 | None -> "no reject")
    | Result.Ok env ->
        let consumer = Versioning.Capability.coreV1
        let expectedNeg = Versioning.negotiate consumer env.Profile

        let gotNeg =
            WireVersioning.negotiate (toModelProfile consumer) (toModelProfile env.Profile)

        let negAgrees =
            match expectedNeg, gotNeg with
            | Versioning.Current, WireVersioning.Current -> true
            | Versioning.Behind a, WireVersioning.Behind b
            | Versioning.Foreign a, WireVersioning.Foreign b -> toModelProfile a = b
            | _ -> false

        let t =
            t
            |> Differential.check negAgrees (fun () ->
                sprintf "%s: production negotiated %A, the model negotiated %A" label expectedNeg gotNeg)

        match expectedNeg with
        | Versioning.Foreign _ ->
            // A Foreign profile is refused at the envelope, so there is no tolerant decode to
            // compare. Recording that here rather than skipping the fixture is what keeps the
            // reject half of the family inside the comparison.
            t
        | _ ->
            let expected =
                Versioning.decodeTolerant envelopeTagOf (fun t -> envelopeVocab.Contains t) Result.Ok env.Payload

            let got =
                WireVersioning.decode_tolerant
                    envelopeTagOfModel
                    (WireVersioning.known_in (toModelVocab envelopeVocab))
                    WireCanon.Ok
                    envelopeRequiredProfileModel
                    (canonToModel env.Payload)

            match expected, got with
            | Result.Error m, WireCanon.Error m' when m = m' -> t
            | Result.Ok d, WireCanon.Ok d' ->
                let shapeAgrees =
                    match d, d' with
                    | Versioning.Known _, WireVersioning.Known _ -> true
                    | Versioning.Unknown u, WireVersioning.Unknown u' ->
                        canonFromChs u'.kind = u.Kind
                        && (match u.RequiredProfile, u'.required_profile with
                            | Some p, FStar_Pervasives_Native.Some p' -> toModelProfile p = p'
                            | None, FStar_Pervasives_Native.None -> true
                            | _ -> false)
                    | _ -> false

                // §15.3 at the bytes. Production's own `Canonical.write Rendering.CrossHost` on both sides — the model's
                // renderer is Phase 149's family's subject, and re-testing it here would be
                // measuring that rather than preservation.
                let expectedBytes = Canonical.write Rendering.CrossHost (Versioning.reencode id d)

                let gotBytes =
                    Canonical.write Rendering.CrossHost (canonOfModel (WireVersioning.reencode id d'))

                t
                |> Differential.check shapeAgrees (fun () ->
                    sprintf "%s: production decoded %A, the model decoded a different shape" label d)
                |> Differential.check
                    (expectedBytes = gotBytes
                     && expectedBytes = Canonical.write Rendering.CrossHost env.Payload)
                    (fun () ->
                        sprintf
                            "%s: preservation — production re-rendered\n  %s\nthe model re-rendered\n  %s\nthe payload was\n  %s"
                            label
                            expectedBytes
                            gotBytes
                            (Canonical.write Rendering.CrossHost env.Payload))
            | _ ->
                Tally.disagree
                    (sprintf "%s: production and the model disagreed on whether the decode succeeded" label)
                    t

[<Tests>]
let tests =
    testList
        "Proofs.Oracle"
        [
          // ---- the evolution policy, over the envelope family and perturbed IDL pairs (Phase 151) ----

          testCase "the versioning oracle agrees with Versioning.classify over perturbed idl.json pairs"
          <| fun _ ->
              let pairs = idlPairs ()

              Tally.empty []
              |> Differential.over pairs (versionProbe WireVersioning.classify)
              |> Differential.expectAgrees "the versioning oracle, idl.json pairs"

              // adequacy — the four rows §15.4 distinguishes must actually have been REACHED, and
              // each must land where the table says. A pass over four pairs that all classified
              // the same way would measure one row four times.
              let verdicts =
                  pairs |> List.map (fun (label, b, a) -> label, Versioning.classify b a)

              let verdictOf name =
                  verdicts |> List.find (fun (l, _) -> l = name) |> snd

              match verdictOf "add a kind" with
              | Versioning.Evolution.Additive [ one ] ->
                  Expect.equal one "Phase151ProbeKind" "the added tag is the one added"
              | other -> failtestf "adding a kind classified as %A" other

              match verdictOf "add an optional field" with
              | Versioning.Evolution.Additive [] -> ()
              | other ->
                  failtestf
                      "adding an OPTIONAL FIELD moved the kind-tag set (%A) — a field addition is invisible AT THIS DELTA by construction, and a verdict here means the perturbation changed a tag. §15.4's optional-field row itself is measured by the field-add case below, which walks the composition that does see it"
                      other

              match verdictOf "remove a tag" with
              | Versioning.Evolution.Breaking(removed, added) ->
                  Expect.equal (List.length removed) 1 "exactly the removed tag"
                  Expect.isEmpty added "removing a tag adds none"
              | other -> failtestf "removing a tag classified as %A" other

              match verdictOf "rename" with
              | Versioning.Evolution.Breaking(removed, added) ->
                  Expect.equal (List.length removed) 1 "a rename removes exactly the old tag"
                  Expect.equal (List.length added) 1 "and adds exactly the new one"
              | other ->
                  failtestf
                      "a RENAME classified as %A — §15.4 calls it breaking, and it is the row an author gets wrong"
                      other

          testCase "the versioning oracle agrees with production over §15.4's three field-add rows"
          <| fun _ ->
              let ms = fieldAddMeasurements ()

              let byClass c =
                  ms |> List.find (fun m -> m.OptClass = c)

              let baseP = Versioning.Capability.coreV1

              // 1. the SEVERITY, clause for clause against `Diff.classifyFieldAdd`.
              for m in ms do
                  Expect.equal
                      m.ModelSeverity
                      m.ProductionSeverity
                      (sprintf "the severity of a `%s` field add" m.OptClass)

              // 2. the PROFILE the revision mints, end to end: the model's own classification, its
              //    own partition into the two subject sets, its own bump — beside
              //    `Diff.bumpProfile`. This is §15.4's actual subject matter, since the table is a
              //    table of BUMPS and the bump is what a publisher acts on.
              for m in ms do
                  Expect.equal
                      m.ModelProfile
                      m.ProductionProfile
                      (sprintf "the profile `core@1.0` bumps to under a `%s` field add" m.OptClass)

              // adequacy — the row is REACHED, and the three classes are told apart. A pass in
              // which all three minted the same profile would be measuring the arrival of a field
              // rather than its optionality class, which is the whole of what this row is about.
              let optional = byClass "optional"

              Expect.equal
                  optional.ProductionProfile
                  (baseP.Name, baseP.Major, baseP.Minor + 1)
                  "§15.4's optional-field row, reached: the MINOR moves and the major does not, so an old consumer is `Behind` and tolerates rather than being told nothing happened"

              Expect.isFalse optional.BreaksEmitters "an added optional field breaks no emitter"

              let required = byClass "required"

              Expect.equal
                  required.ProductionProfile
                  (baseP.Name, baseP.Major + 1, 0)
                  "a REQUIRED field add moves the MAJOR (Phase 304) — every old document lacks the member and is refused, so an old consumer must be `Foreign`, not a `Behind` that tolerates and then refuses"

              Expect.isFalse
                  required.BreaksEmitters
                  "and it is not carried as a mere emitter break beside a minor: the old documents themselves are what break"

              let hostOnly = byClass "hostOnly"

              Expect.equal
                  hostOnly.ProductionProfile
                  (baseP.Name, baseP.Major, baseP.Minor)
                  "a HOST-ONLY field is on no document in either direction (WIRE_FORMAT §9), so no profile may move"

              // the go-red — the model's own `classify_field_add_ignoring_optionality`, which is
              // what an author writes who reads "an added field is additive" and stops. It agrees
              // on the row this phase is named for, which is exactly why the comparison has to
              // reach the other two.
              Expect.notEqual
                  hostOnly.GoRedProfile
                  hostOnly.ProductionProfile
                  "a classifier that ignores the optionality class publishes a minor for a field that is on no document — the comparison can fail"

              Expect.notEqual
                  required.GoRedProfile
                  required.ProductionProfile
                  "and a minor for a field every old document lacks, where production moves the major (Phase 304)"

              Expect.equal
                  optional.GoRedProfile
                  optional.ProductionProfile
                  "and it agrees on the optional row, which is what makes it a go-red worth having rather than a strawman"

          testCase "the versioning oracle agrees with production over every envelope/ fixture"
          <| fun _ ->
              let fixtures = JsonParseDiff.corpusTexts "envelope"
              let rejects = envelopeRejects ()

              Tally.empty []
              |> Differential.over fixtures (fun (name, text) t ->
                  envelopeProbe rejects (sprintf "envelope/%s" name) text t)
              |> Differential.expectAgrees "the versioning oracle, envelope/"

              Expect.isGreaterThan
                  (List.length fixtures)
                  4
                  (sprintf "the envelope/ family was read at all (%d fixtures)" (List.length fixtures))

              // adequacy — all three negotiation outcomes must have been reached, and at least one
              // fixture must have carried a tag the consumer does NOT know. A family that only ever
              // produced `Current` would exercise neither tolerance nor preservation, and the run
              // would be green having tested nothing §15.3 is about.
              let outcomes =
                  fixtures
                  |> List.choose (fun (_, text) ->
                      match Versioning.parse text with
                      | Result.Ok env -> Some(Versioning.negotiate Versioning.Capability.coreV1 env.Profile)
                      | Result.Error _ -> None)

              Expect.isTrue
                  (outcomes
                   |> List.exists (function
                       | Versioning.Current -> true
                       | _ -> false))
                  "a Current fixture was reached"

              Expect.isTrue
                  (outcomes
                   |> List.exists (function
                       | Versioning.Behind _ -> true
                       | _ -> false))
                  "a Behind fixture was reached — otherwise tolerance is untested"

              Expect.isTrue
                  (outcomes
                   |> List.exists (function
                       | Versioning.Foreign _ -> true
                       | _ -> false))
                  "a Foreign fixture was reached — otherwise the refusal half is untested"

              let unknowns =
                  fixtures
                  |> List.filter (fun (_, text) ->
                      match Versioning.parse text with
                      | Result.Ok env ->
                          match envelopeTagOf env.Payload with
                          | Result.Ok t -> not (envelopeVocab.Contains t)
                          | Result.Error _ -> false
                      | Result.Error _ -> false)

              Expect.isGreaterThan
                  (List.length unknowns)
                  1
                  (sprintf
                      "fixtures carrying a tag the consumer does NOT know (%d) — preservation is unobservable without one"
                      (List.length unknowns))

          testCase "the versioning oracle agrees with Versioning.tryBump and bump at the edge of the counter range"
          <| fun _ ->
              // Phase 306. A counter is an Int32; at its last value the bump has no successor.
              // The model refuses there (`try_bump`) and saturates (`bump`), as production does —
              // where `minor + 1` used to wrap negative and the model, over `nat`, could not see it.
              let counters = [ 0; 1; System.Int32.MaxValue - 1; System.Int32.MaxValue ]

              let evolutions =
                  [ Versioning.Evolution.Additive [], WireVersioning.Additive []
                    Versioning.Evolution.Additive [ "x" ], WireVersioning.Additive [ canonToChs "x" ]
                    Versioning.Evolution.Breaking([ "x" ], [ "y" ]),
                    WireVersioning.Breaking([ canonToChs "x" ], [ canonToChs "y" ]) ]

              let cases =
                  [ for major in counters do
                        for minor in counters do
                            for (ev, modelEv) in evolutions do
                                ({ Name = "core"
                                   Major = major
                                   Minor = minor }
                                : Versioning.Capability),
                                ev,
                                modelEv ]

              let t =
                  Tally.empty [ "Refused"; "Bumped" ]
                  |> Differential.over cases (fun (p, ev, modelEv) t ->
                      let saturated = Versioning.bump p ev

                      Expect.isTrue
                          (saturated.Major >= 0 && saturated.Minor >= 0)
                          (sprintf "bump %A %A stays a profile the wire can carry" p ev)

                      let t =
                          match Versioning.tryBump p ev, WireVersioning.try_bump (toModelProfile p) modelEv with
                          | Result.Ok q, FStar_Pervasives_Native.Some q' ->
                              t
                              |> Tally.bump "Bumped"
                              |> Differential.compare
                                  Differential.structural
                                  (sprintf "tryBump %A %A" p ev)
                                  (toModelProfile q)
                                  q'
                          | Result.Error _, FStar_Pervasives_Native.None -> Tally.bump "Refused" t
                          | got, model ->
                              Tally.disagree (sprintf "tryBump %A %A: production %A, the model %A" p ev got model) t

                      t
                      |> Differential.compare
                          Differential.structural
                          (sprintf "bump %A %A" p ev)
                          (toModelProfile saturated)
                          (WireVersioning.bump (toModelProfile p) modelEv))

              Differential.expectAgrees "tryBump and bump at the edge of the counter range" t

              // An additive step is refused at each of the four majors where the minor is at the
              // limit, a breaking one at each of the four minors where the major is: eight.
              Expect.equal (Tally.count "Refused" t) 8 "the refusals are exactly the edge"
              Expect.equal (Tally.count "Bumped" t) 40 "and every other step bumps"

          testCase "a model whose classify IGNORES REMOVALS loses — the comparison can fail"
          <| fun _ ->
              // The go-red. `classify_sound` turns entirely on removals being looked for, so the
              // instrument that must lose is one that does not look — the model's own
              // `classify_ignoring_removals`, which F* refutes in the same file. Every pair with a
              // removal must disagree with production; every purely additive pair must still agree,
              // which is what says the instrument is narrow to the rule rather than broken.
              let pairs = idlPairs ()

              let diffs =
                  Tally.empty []
                  |> Differential.over pairs (versionProbe WireVersioning.classify_ignoring_removals)
                  |> fun t ->
                      Differential.expectLoses
                          "a classifier that never looks for removals, against Versioning.classify"
                          t

                      Tally.disagreements t

              Expect.isLessThan
                  (List.length diffs)
                  (List.length pairs)
                  (sprintf
                      "the removal-blind classifier disagreed on %d of %d pairs — it must leave the purely additive ones alone"
                      (List.length diffs)
                      (List.length pairs))

              // and precisely which two: the removal and the rename, which is the whole of §15.4's
              // breaking column. Naming them is what separates "it went red" from "it went red for
              // the reason the theorem is about".
              let failedLabels =
                  diffs |> List.map (fun d -> d.Substring(0, d.IndexOf ':')) |> List.sort

              Expect.equal failedLabels [ "remove a tag"; "rename" ] "exactly the two breaking rows are where it loses" ]
