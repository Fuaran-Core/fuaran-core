namespace Fuaran.Core

// Phase 184 — the law-family ROSTER, exported by the kit as data.
//
// Until this module the kit shipped its families and enumerated them nowhere. Three separate
// readers each kept their own list, and each list was derived by a rule that could miss a family:
//
//   * `SampleAdequacy.census` classifies a family's sample, and its completeness check reflected
//     over method NAMES ending in `Laws` / `LawsWith` / `laws` / `lawsWith` — so `opAlgebra`,
//     `reducer` and `compositionPilot` were invisible to it. Two of those three are the families
//     `certify` and `certifyStream` are BUILT FROM: the most central laws in the kit were the ones
//     the roster could not see.
//   * the claims ladder's `dischargedBy` was held to every public static method of `Conformance`,
//     which is far wider than the law set — so a row could name a helper and pass.
//   * a consumer's generated conformance census, and the projection that reads it, quantify over
//     whatever roster they are handed; a family absent from it is reported unrostered by every
//     consumer and can never be marked adopted by any of them.
//
// A missing row in any of those is silent by construction: every check quantifies over the list,
// so the one thing a list cannot notice is a family nobody added to it. This module is the single
// declared enumeration all three now read, and `ConformanceFamiliesTests` holds it to REFLECTION
// OVER RETURN TYPE — every public entry point of the kit's modules that answers with
// `LawResult list` — rather than over a naming convention. The name shape is what let the three
// escape; the return type is what a law family actually is.
//
// It declares, it does not derive. What each family IS — its witness demands, whether an aggregate
// runs it, which ladder obligation its green run discharges — is a fact about the code that only a
// reader can state; the suite's job is to hold every one of those statements to the tree, and to
// refuse the roster that is merely incomplete. See `docs/conformance-families.md` (generated) and
// the `docs/conformance-families.json` export beside it.

/// The kit's own enumeration of the law families it ships.
///
/// PUBLIC SURFACE. A host reads it to enumerate the families it must answer for; a projection
/// reads the generated JSON export to do the same offline. Adding a law family to the kit means
/// adding a record here in the same commit — the suite fails naming the family otherwise.
module Families =

    /// WHY a family is opt-in — the closed vocabulary, one case per reason the roster actually
    /// carries. It is a DU rather than a string because the set is closed and a reader dispatches
    /// on it; a free-text reason would be a second place for prose to drift from the code.
    ///
    /// **`NeedsSecondFixture` is deliberately absent** (Phase 194). The shard proposed it, and no
    /// family instantiates it: the families that need nothing from the domain need NO fixture at
    /// all rather than a second one, and they are `SeamNotEveryDomainHas`. A case no value inhabits
    /// is a case a reader has to rule out on every encounter, so it is not declared.
    type OptInReason =
        /// The family demands a witness, generator or sink the BASE contract does not — an
        /// `ArtifactWitness`, an `IAttestationSink`, a `LaneGen`. A domain that has not built that
        /// capability cannot run it at all, so `certify` cannot fold it in.
        | NeedsWitnessCapability
        /// The family certifies a SEAM not every domain has — a columnar layer, a capability
        /// registry, a lease axis. It runs from the kit's own fixtures and needs nothing from the
        /// domain, so what makes it opt-in is relevance, never cost.
        | SeamNotEveryDomainHas
        /// The family takes exactly the base run's witness and asks for MORE than the base
        /// contract promises — footprint independence, concurrent apply, arbitration. A domain
        /// elects it; the base contract does not imply it.
        | StrongerPromise

    /// One law family: a public entry point of the kit that answers with `LawResult list`.
    type LawFamily =
        {
            /// The roster key — `"<Module>.<Entry>"`. This is the name a census cell, a ladder
            /// row's `dischargedBy` and a projection's roster all use, so it is the one spelling
            /// that must not drift.
            Id: string
            /// The kit module the entry point lives in (`Conformance`, `FoldConfluence`,
            /// `IncrementalDelta`).
            Module: string
            /// The entry point's own name, unqualified.
            Entry: string
            /// The witness and generator types a domain must supply to run it, in parameter
            /// order — every parameter whose type name ends in `Witness`, `Gen` or `Sink`. Empty
            /// for a family the kit runs against its own fixtures, which needs nothing but a seed.
            Witness: string list
            /// `true` when a domain must call the family DELIBERATELY: it is not folded into
            /// `Conformance.certify` or `Conformance.certifyStream`, because it certifies a seam
            /// not every domain has. `false` for the five families an aggregate is built from.
            ///
            /// DERIVED from `Reason` at every construction site in this module — `OptIn` is
            /// `Reason.IsSome` — so "opt-in with no reason" is unrepresentable here rather than
            /// merely caught by a test. The field stays published because it is what every reader
            /// of the roster already dispatches on.
            OptIn: bool
            /// Why the family is opt-in, `None` for a base-run family. Phase 194: a census that
            /// says a consumer did not run a family is only actionable if the roster says why the
            /// family was theirs to elect.
            Reason: OptInReason option
            /// The claims-ladder obligations (`proofs.json` row ids) whose `dischargedBy` names
            /// this family — the rows a green run of it at a domain's own witness discharges.
            /// The ladder is the other side of the same relation and the suite holds the two
            /// equal, so this is an index rather than a second declaration.
            Discharges: string list
        }

    /// Every law family the kit ships, in declaration order. Renderings sort by `Id`, so the order
    /// here is for a reader's benefit and never reaches an artefact.
    let families: LawFamily list =
        // `reason` is `OptInReason option`; `OptIn` is derived from it, so a family cannot be
        // declared opt-in without saying why, and a base-run family cannot carry a reason.
        let f m entry witness reason discharges =
            { Id = m + "." + entry
              Module = m
              Entry = entry
              Witness = witness
              OptIn = Option.isSome reason
              Reason = reason
              Discharges = discharges }

        let c entry witness reason discharges =
            f "Conformance" entry witness reason discharges

        let treeWitness = [ "NodeWitness"; "IdWitness"; "OpGen" ]
        let streamWitness = [ "StreamWitness"; "StreamGen" ]
        let none: string list = []

        [
          // ---- the base run: what `certify` and `certifyStream` are built from ----
          c "witnessLaws" treeWitness None [ "lawful-abstract-witness" ]
          c "opAlgebra" treeWitness None [ "tree-algebra-well-formed-states" ]
          c "diffLaws" treeWitness None []
          c "streamLaws" streamWitness None []
          c "reducer" [ "StreamGen" ] None []

          // ---- opt-in: a seam not every domain has ----
          c "diffContainedLaws" treeWitness (Some StrongerPromise) []
          c "normalizeLaws" treeWitness (Some StrongerPromise) []
          c "containerLaws" treeWitness (Some StrongerPromise) []
          c "mergeConflictLaws" treeWitness (Some StrongerPromise) []
          c "reconcileLaws" treeWitness (Some StrongerPromise) []
          c "footprintLaws" treeWitness (Some StrongerPromise) [ "independence-diamond" ]
          c "concurrencyLaws" treeWitness (Some StrongerPromise) [ "lanes-apply" ]
          c "concurrencyLawsWith" treeWitness (Some StrongerPromise) []
          c "arbitrationLaws" treeWitness (Some StrongerPromise) []
          c "snapshotLaws" streamWitness (Some StrongerPromise) []
          c "snapshotLawsWith" streamWitness (Some StrongerPromise) []
          c "dagLaws" streamWitness (Some StrongerPromise) []
          c "casLaws" streamWitness (Some StrongerPromise) []
          c "idempotencyLaws" streamWitness (Some StrongerPromise) []
          c "hashFnLaws" streamWitness (Some StrongerPromise) []
          c "attributedLaws" streamWitness (Some StrongerPromise) []
          c "codecInjectivityLaws" streamWitness (Some StrongerPromise) []
          c "noAttestationVacuityLaws" streamWitness (Some StrongerPromise) []
          c "attestationLaws" [ "StreamWitness"; "StreamGen"; "IAttestationSink" ] (Some NeedsWitnessCapability) []
          c "compositionLaws" [ "ArtifactWitness" ] (Some NeedsWitnessCapability) []
          c "compositionPilot" [ "ArtifactWitness" ] (Some NeedsWitnessCapability) []
          c "memoLaws" [ "ArtifactWitness" ] (Some NeedsWitnessCapability) []
          c "memoSoundnessLaws" [ "ArtifactWitness" ] (Some NeedsWitnessCapability) []
          c "functionVerifyLaws" [ "ArtifactWitness" ] (Some NeedsWitnessCapability) []
          c "verifyHonestyLaws" [ "ArtifactWitness" ] (Some NeedsWitnessCapability) []
          c "encoderInjectivityLaws" [ "ArtifactWitness" ] (Some NeedsWitnessCapability) []
          c "projectionLaws" [ "ProjectionWitness" ] (Some NeedsWitnessCapability) []
          c "aiSurfaceLaws" [ "AiSurfaceWitness" ] (Some NeedsWitnessCapability) []

          c
              "keyedChildrenLaws"
              ([ "KeyedWitness" ] @ treeWitness)
              (Some NeedsWitnessCapability)
              [ "witness-surface-scope" ]

          c "captureReplayLaws" none (Some SeamNotEveryDomainHas) []
          c "transformLaws" none (Some SeamNotEveryDomainHas) []
          c "constructThenEncodeLaws" none (Some SeamNotEveryDomainHas) []
          c "hashFnAdversarialLaws" none (Some SeamNotEveryDomainHas) []
          c "capabilityLaws" none (Some SeamNotEveryDomainHas) []
          c "queryLaws" none (Some SeamNotEveryDomainHas) []
          c "registryLaws" none (Some SeamNotEveryDomainHas) []
          c "packLoadingLaws" none (Some SeamNotEveryDomainHas) []
          c "aggregateParityLaws" none (Some SeamNotEveryDomainHas) []
          c "columnarOpLaws" none (Some SeamNotEveryDomainHas) []
          c "columnarOpLawsWith" none (Some SeamNotEveryDomainHas) []
          c "columnarValidatorLaws" none (Some SeamNotEveryDomainHas) []
          c "incrementalLaws" none (Some SeamNotEveryDomainHas) []
          c "paramLaws" none (Some SeamNotEveryDomainHas) []
          c "schemaWalkLaws" none (Some SeamNotEveryDomainHas) []
          c "deferredLaws" none (Some SeamNotEveryDomainHas) []
          c "capabilityPipelineLaws" none (Some SeamNotEveryDomainHas) []
          c "capabilityPipelineIncrementalLaws" none (Some SeamNotEveryDomainHas) []
          c "dirtyPropagationLaws" none (Some SeamNotEveryDomainHas) []
          c "propagationEvalLaws" none (Some SeamNotEveryDomainHas) []
          c "canonicalFloatLaws" none (Some SeamNotEveryDomainHas) []
          c "leaseLaws" none (Some SeamNotEveryDomainHas) []
          c "chainBreakReasonLaws" none (Some SeamNotEveryDomainHas) []
          c "dagBreakReasonLaws" none (Some SeamNotEveryDomainHas) []
          c "nowLaws" none (Some SeamNotEveryDomainHas) []
          c "slotParamLaws" none (Some SeamNotEveryDomainHas) []

          f "FoldConfluence" "laneFoldLaws" [ "StreamWitness"; "LaneGen" ] (Some NeedsWitnessCapability) []
          f "FoldConfluence" "laneFoldLawsWith" [ "StreamWitness"; "LaneGen" ] (Some NeedsWitnessCapability) []

          f "IncrementalDelta" "laws" none (Some SeamNotEveryDomainHas) []
          f "IncrementalDelta" "lawsWith" none (Some SeamNotEveryDomainHas) [] ]

    /// The roster's keys, sorted — the enumeration a census, a ladder or a projection quantifies
    /// over.
    let ids: string list = families |> List.map (fun f -> f.Id) |> List.sort

    /// The modules the roster covers, sorted. A reflection check reads THIS rather than a second
    /// list, so a module added to the kit is covered by adding its families here and nothing else.
    let modules: string list =
        families |> List.map (fun f -> f.Module) |> List.distinct |> List.sort

    /// The family with this id, if the kit ships one.
    let tryFind (id: string) : LawFamily option =
        families |> List.tryFind (fun f -> f.Id = id)

    /// Every ladder obligation the roster claims to discharge, paired with the family that
    /// discharges it, sorted by obligation.
    let obligations: (string * string) list =
        [ for f in families do
              for o in f.Discharges -> o, f.Id ]
        |> List.sortBy fst

    // ---- the exports ------------------------------------------------------------------------

    let private quote (s: string) : string =
        let esc (c: char) =
            match c with
            | '"' -> "\\\""
            | '\\' -> "\\\\"
            | '\n' -> "\\n"
            | '\r' -> "\\r"
            | '\t' -> "\\t"
            | c when c < ' ' -> "\\u" + (int c).ToString "x4"
            | c -> string c

        "\"" + (s |> Seq.map esc |> String.concat "") + "\""

    let private jsonArray (xs: string list) : string =
        "[" + (xs |> List.map quote |> String.concat ", ") + "]"

    /// The wire spelling of an opt-in reason — the JSON member and the markdown cell both use
    /// it, so the two renderings never disagree about a family. A base-run family has none.
    let reasonToken (r: OptInReason) : string =
        match r with
        | NeedsWitnessCapability -> "needs-witness-capability"
        | SeamNotEveryDomainHas -> "seam-not-every-domain-has"
        | StrongerPromise -> "stronger-promise"

    /// The roster as JSON — the machine export, and the one an offline projection reads without
    /// building or running anything (it is committed, generated, at `docs/conformance-families.json`).
    ///
    /// The shape is a CONTRACT and is documented in `STABILITY.md`: a top-level object carrying
    /// `kind`, `schema`, `package` and a `families` array sorted by `id`, each member an object
    /// with `id`, `module`, `entry`, `witness` (array), `optIn` (boolean), `reason` (a string from
    /// the [[OptInReason]] vocabulary, **present only for an opt-in family** — this wire model has
    /// no null) and `discharges` (array).
    ///
    /// `schema` reads 2 since Phase 194 added `reason`. The bump is free and therefore taken: a
    /// search of the workspace found no reader of this file outside this repository's own suite,
    /// so nothing keys on the old number, and a shape that changes under an unmoved stamp is the
    /// drift class this estate keeps paying for elsewhere.
    /// Members are written in that order and the array is sorted, so the rendering is byte-stable
    /// across runs and a diff shows only what moved. Two spaces of indent, `\n` line endings, and
    /// a trailing newline.
    let toJson () : string =
        let family (f: LawFamily) =
            // `reason` is OMITTED for a base-run family rather than rendered `null`: this wire
            // model has no null (`JVal` cannot represent one, and `no_null_ever` is a proved
            // grammar theorem over the renderer), so a null here would emit a document the kit's
            // own parser refuses. Absence is how this format spells "not applicable".
            [ "      " + quote "id" + ": " + quote f.Id
              "      " + quote "module" + ": " + quote f.Module
              "      " + quote "entry" + ": " + quote f.Entry
              "      " + quote "witness" + ": " + jsonArray f.Witness
              "      " + quote "optIn" + ": " + (if f.OptIn then "true" else "false")
              "      " + quote "discharges" + ": " + jsonArray f.Discharges ]
            |> fun members ->
                match f.Reason with
                | None -> members
                | Some r ->
                    // after `optIn`, before `discharges` — the documented member order
                    let head, tail = List.splitAt 5 members
                    head @ [ "      " + quote "reason" + ": " + quote (reasonToken r) ] @ tail
            |> String.concat ",\n"
            |> fun body -> "    {\n" + body + "\n    }"

        let body =
            families
            |> List.sortBy (fun f -> f.Id)
            |> List.map family
            |> String.concat ",\n"

        "{\n"
        + "  "
        + quote "kind"
        + ": "
        + quote "fuaran.core.conformance.families"
        + ",\n"
        + "  "
        + quote "schema"
        + ": 2,\n"
        + "  "
        + quote "package"
        + ": "
        + quote "Fuaran.Core.Conformance"
        + ",\n"
        + "  "
        + quote "families"
        + ": [\n"
        + body
        + "\n  ]\n}\n"

    /// The roster as the generated `docs/conformance-families.md` — the human-readable half of the
    /// same export. Sorted by `id`, so the table is byte-stable and a diff shows only what moved.
    let toMarkdown () : string =
        let cell (xs: string list) =
            if List.isEmpty xs then
                "—"
            else
                xs |> List.map (fun x -> "`" + x + "`") |> String.concat ", "

        let row (f: LawFamily) =
            sprintf
                "| `%s` | %s | %s | %s | %s |"
                f.Id
                (if f.OptIn then "opt-in" else "base run")
                (match f.Reason with
                 | Some r -> "`" + reasonToken r + "`"
                 | None -> "—")
                (cell f.Witness)
                (cell f.Discharges)

        let rows = families |> List.sortBy (fun f -> f.Id) |> List.map row

        [ "# The law families this kit ships"
          ""
          "_GENERATED from `Fuaran.Core.Families`. Do not hand-edit — the suite compares this file"
          "against the roster and names the command that rewrites it:_"
          ""
          "```"
          "dotnet run --project tests/Fuaran.Core.Tests -- --emit-families"
          "```"
          ""
          "_The machine-readable half is `conformance-families.json` beside it; `STABILITY.md`"
          "documents its shape._"
          ""
          "A **law family** is a public entry point of this kit that answers with `LawResult list`."
          "Running one at your own witness is how a domain certifies it conforms; this table is the"
          "enumeration of what there is to run, so a family cannot be quietly absent from a"
          "conformance census that quantifies over it. The enumeration is held to reflection over"
          "the shipped assembly BY RETURN TYPE, so it cannot miss a family by how the family is"
          "named."
          ""
          "**Base run / opt-in.** The five `base run` families are the ones `Conformance.certify` and"
          "`Conformance.certifyStream` are built from — a domain gets them by calling an aggregate."
          "Every other family certifies a seam not every domain has, so a domain calls it"
          "deliberately, alongside its base run. Neither is a statement about importance: `reducer`"
          "is a base-run family only for stream-shaped domains, and `footprintLaws` is opt-in while"
          "discharging a ladder obligation."
          ""
          "**Witness.** The witness and generator types the entry point takes, in parameter order. A"
          "family with none runs against the kit's own fixtures and needs nothing but a seed."
          ""
          "**Discharges.** The claims-ladder obligations (`proofs.json` row ids) a green run of the"
          "family at your own witness discharges. Most families discharge none — they certify, they"
          "do not answer for an assumption this repository's proofs leave open."
          ""
          sprintf
              "%d families, across %s."
              (List.length families)
              (modules |> List.map (fun m -> "`" + m + "`") |> String.concat ", ")
          ""
          "| Family | Run by | Why opt-in | Witness | Discharges |"
          "|---|---|---|---|---|" ]
        @ rows
        @ [ "" ]
        |> String.concat "\n"
