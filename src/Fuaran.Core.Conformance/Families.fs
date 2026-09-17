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
            OptIn: bool
            /// The claims-ladder obligations (`proofs.json` row ids) whose `dischargedBy` names
            /// this family — the rows a green run of it at a domain's own witness discharges.
            /// The ladder is the other side of the same relation and the suite holds the two
            /// equal, so this is an index rather than a second declaration.
            Discharges: string list
        }

    /// Every law family the kit ships, in declaration order. Renderings sort by `Id`, so the order
    /// here is for a reader's benefit and never reaches an artefact.
    let families: LawFamily list =
        let f m entry witness optIn discharges =
            { Id = m + "." + entry
              Module = m
              Entry = entry
              Witness = witness
              OptIn = optIn
              Discharges = discharges }

        let c entry witness optIn discharges =
            f "Conformance" entry witness optIn discharges

        let treeWitness = [ "NodeWitness"; "IdWitness"; "OpGen" ]
        let streamWitness = [ "StreamWitness"; "StreamGen" ]
        let none: string list = []

        [
          // ---- the base run: what `certify` and `certifyStream` are built from ----
          c "witnessLaws" treeWitness false [ "lawful-abstract-witness" ]
          c "opAlgebra" treeWitness false [ "tree-algebra-well-formed-states" ]
          c "diffLaws" treeWitness false []
          c "streamLaws" streamWitness false []
          c "reducer" [ "StreamGen" ] false []

          // ---- opt-in: a seam not every domain has ----
          c "diffContainedLaws" treeWitness true []
          c "normalizeLaws" treeWitness true []
          c "containerLaws" treeWitness true []
          c "mergeConflictLaws" treeWitness true []
          c "reconcileLaws" treeWitness true []
          c "footprintLaws" treeWitness true [ "independence-diamond" ]
          c "concurrencyLaws" treeWitness true [ "lanes-apply" ]
          c "concurrencyLawsWith" treeWitness true []
          c "arbitrationLaws" treeWitness true []
          c "snapshotLaws" streamWitness true []
          c "snapshotLawsWith" streamWitness true []
          c "dagLaws" streamWitness true []
          c "casLaws" streamWitness true []
          c "idempotencyLaws" streamWitness true []
          c "hashFnLaws" streamWitness true []
          c "attributedLaws" streamWitness true []
          c "codecInjectivityLaws" streamWitness true []
          c "noAttestationVacuityLaws" streamWitness true []
          c "attestationLaws" [ "StreamWitness"; "StreamGen"; "IAttestationSink" ] true []
          c "compositionLaws" [ "ArtifactWitness" ] true []
          c "compositionPilot" [ "ArtifactWitness" ] true []
          c "memoLaws" [ "ArtifactWitness" ] true []
          c "memoSoundnessLaws" [ "ArtifactWitness" ] true []
          c "functionVerifyLaws" [ "ArtifactWitness" ] true []
          c "verifyHonestyLaws" [ "ArtifactWitness" ] true []
          c "encoderInjectivityLaws" [ "ArtifactWitness" ] true []
          c "projectionLaws" [ "ProjectionWitness" ] true []
          c "aiSurfaceLaws" [ "AiSurfaceWitness" ] true []
          c "captureReplayLaws" none true []
          c "transformLaws" none true []
          c "constructThenEncodeLaws" none true []
          c "hashFnAdversarialLaws" none true []
          c "capabilityLaws" none true []
          c "queryLaws" none true []
          c "registryLaws" none true []
          c "packLoadingLaws" none true []
          c "aggregateParityLaws" none true []
          c "columnarOpLaws" none true []
          c "columnarOpLawsWith" none true []
          c "columnarValidatorLaws" none true []
          c "incrementalLaws" none true []
          c "paramLaws" none true []
          c "schemaWalkLaws" none true []
          c "deferredLaws" none true []
          c "capabilityPipelineLaws" none true []
          c "capabilityPipelineIncrementalLaws" none true []
          c "dirtyPropagationLaws" none true []
          c "propagationEvalLaws" none true []
          c "canonicalFloatLaws" none true []
          c "leaseLaws" none true []
          c "chainBreakReasonLaws" none true []
          c "dagBreakReasonLaws" none true []
          c "nowLaws" none true []
          c "slotParamLaws" none true []

          f "FoldConfluence" "laneFoldLaws" [ "StreamWitness"; "LaneGen" ] true []
          f "FoldConfluence" "laneFoldLawsWith" [ "StreamWitness"; "LaneGen" ] true []

          f "IncrementalDelta" "laws" none true []
          f "IncrementalDelta" "lawsWith" none true [] ]

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

    /// The roster as JSON — the machine export, and the one an offline projection reads without
    /// building or running anything (it is committed, generated, at `docs/conformance-families.json`).
    ///
    /// The shape is a CONTRACT and is documented in `STABILITY.md`: a top-level object carrying
    /// `kind`, `schema`, `package` and a `families` array sorted by `id`, each member an object
    /// with `id`, `module`, `entry`, `witness` (array), `optIn` (boolean) and `discharges` (array).
    /// Members are written in that order and the array is sorted, so the rendering is byte-stable
    /// across runs and a diff shows only what moved. Two spaces of indent, `\n` line endings, and
    /// a trailing newline.
    let toJson () : string =
        let family (f: LawFamily) =
            [ "      " + quote "id" + ": " + quote f.Id
              "      " + quote "module" + ": " + quote f.Module
              "      " + quote "entry" + ": " + quote f.Entry
              "      " + quote "witness" + ": " + jsonArray f.Witness
              "      " + quote "optIn" + ": " + (if f.OptIn then "true" else "false")
              "      " + quote "discharges" + ": " + jsonArray f.Discharges ]
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
        + ": 1,\n"
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
                "| `%s` | %s | %s | %s |"
                f.Id
                (if f.OptIn then "opt-in" else "base run")
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
          "| Family | Run by | Witness | Discharges |"
          "|---|---|---|---|" ]
        @ rows
        @ [ "" ]
        |> String.concat "\n"
