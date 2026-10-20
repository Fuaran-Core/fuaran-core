namespace Fuaran.Core.Idl.Emit

open Fuaran.Core
open Fuaran.Core.Idl
open Fuaran.Core.Idl.Codegen.Types

/// The stability classifier's RULES (Phase 293's descriptor table; Phase 524 made it one match):
/// for every `Change`, its sort rank and key, the §11 family it reaches, its wire verdict, its F#
/// consequence, its summary and the `docs/` rows it shows. Every other classifier module reads a
/// change through [[ruleOf]].
module internal DiffRules =

    /// The stable label of a consequence class. A contract: these strings are what
    /// an external gate greps and what the `docs/` table names.
    let consequenceLabel =
        function
        | FullLiteralConstruction -> "full-literal-construction"
        | ExhaustiveMatch -> "exhaustive-match"
        | TypeNameReference -> "type-name-reference"
        | StalePackageSlot -> "stale-package-slot"
        | NoGeneratedShapeChange -> "no-generated-shape-change"
        | GeneratedShapeUnreadable -> "generated-shape-unreadable"

    /// One sentence per consequence class — the reason it applies, for a report that
    /// has to stand on its own beside a compiler message.
    let consequenceWhy =
        function
        | FullLiteralConstruction ->
            "every full record literal that builds this owner stops compiling — FS0764 on an added field, FS1129 on a removed one, FS0001 on a moved type. True for an OPTIONAL field too: a literal must name it."
        | ExhaustiveMatch ->
            "every exhaustive match over the generated DU stops being exhaustive — FS0025 (a warning by default, an error under TreatWarningsAsErrors, a MatchFailureException at run time) on an added case, FS0039 on a removed one."
        | TypeNameReference ->
            "a generated type name no longer resolves, or no longer takes the arity a consumer applies — FS0039."
        | StalePackageSlot ->
            "no compile event at all if the package slot was REPACKED rather than advanced: a consumer restoring the same version from a warm cache compiles against the new shape and runs against the old one, and the mismatch arrives as an InvalidCastException from code that type-checked."
        | NoGeneratedShapeChange ->
            "the generated declarations are unchanged, so no construction, match or reference site moves."
        | GeneratedShapeUnreadable ->
            "the change crosses an erased slot whose admitted values the artifact does not state, so whether the generated shape moved cannot be read off it."

    /// Every consequence class, in report order — so a renderer and a document
    /// enumerate the table rather than each restating it.
    let allConsequences: FSharpConsequence list =
        [ FullLiteralConstruction
          ExhaustiveMatch
          TypeNameReference
          StalePackageSlot
          NoGeneratedShapeChange
          GeneratedShapeUnreadable ]

    /// Which `WIRE_FORMAT.md` §11 strand a change reaches — the three predicates the host-roster
    /// join reads.
    type Family =
        /// Alters the NodeKind set: reaches the authoring veneers, the analyzer vocabulary, the
        /// native render arms and `manifest.kinds`.
        | KindSet
        /// Alters a `$type` discriminator family OTHER than NodeKind.
        | DiscriminatorFamily
        /// Alters a closed string set.
        | EnumSet
        | NoFamily

    /// One row of the `docs/` mapping table.
    type DocRow =
        { Subject: string
          Wire: string
          FSharp: string }

    /// What a snapshot pair knows about a kind's field that the `Change` does not carry: its
    /// presence (`Some` when a snapshot has the field), and whether an authoring default is
    /// declared for it on either side.
    type Context =
        { Presence: string -> string -> PresenceSnap option
          HasDefault: string -> string -> bool }

    /// The context-free reading — no snapshot to ask.
    let noContext: Context =
        { Presence = fun _ _ -> None
          HasDefault = fun _ _ -> false }

    /// Phase 524 — one change's descriptor: everything the classifier decides about it, computed by
    /// [[ruleOf]], the ONE match over `Change`. `classify`, `consequences`, `sortKey`, the family
    /// predicates, `summarise` and `mappingTable` are projections of it, so a rule corrected once is
    /// corrected everywhere it is read (Phase 293's table, whose rows were closures joined to the
    /// union by reflection, each re-matching the case it was filed under).
    type Rule =
        {
            /// The `Change` case's name, as the `docs/` mapping table cites it.
            Case: string
            /// The sort rank of the case.
            Rank: string
            /// The §11 family the change reaches.
            Family: Family
            /// The change's own sort key within its rank.
            Key: string
            /// The wire verdict: severity, rationale, citation.
            Verdict: Severity * string * string
            /// The F# consequence set, given what a snapshot pair knows about the change's field.
            Consequence: Context -> FSharpConsequence list
            /// The one-line summary a report heads the change with.
            Summary: string
            /// The rows the `docs/` mapping table shows for the case.
            Doc: DocRow list
        }

    let private rule
        (case: string)
        (rank: string)
        (family: Family)
        (key: string)
        (verdict: Severity * string * string)
        (consequence: Context -> FSharpConsequence list)
        (summary: string)
        (doc: DocRow list)
        : Rule =
        { Case = case
          Rank = rank
          Family = family
          Key = key
          Verdict = verdict
          Consequence = consequence
          Summary = summary
          Doc = doc }

    let private construction = [ FullLiteralConstruction; StalePackageSlot ]

    let private matching = [ ExhaustiveMatch; StalePackageSlot ]

    let private reference = [ TypeNameReference; StalePackageSlot ]

    let private noShape = [ NoGeneratedShapeChange ]

    let private erasedTag (t: string) =
        t = "hosted" || t = "json" || t = "opaque"

    /// Phase 293 — the erased-slot rule walks NESTED slots: a list element, a map value or a
    /// union argument that is `hosted` / `json` / `opaque` is as undecidable as a bare one, and
    /// the rule used to test the top-level tag alone, so a nested crossing read as `breaking-wire`
    /// where the document said undecided. Returns the first erased tag met, outermost first.
    let rec private erasedWithin (v: JVal) : string option =
        match v with
        | JObj fs ->
            let own =
                fs
                |> List.tryPick (fun (k, x) ->
                    match k, x with
                    | "$type", JStr t when erasedTag t -> Some t
                    | _ -> None)

            match own with
            | Some t -> Some t
            | None -> fs |> List.tryPick (fun (_, x) -> erasedWithin x)
        | JArr xs -> xs |> List.tryPick erasedWithin
        | _ -> None

    /// The erased tag a field's wire type crosses, if any — its own, or one nested in it.
    let private erasedIn (f: FieldSnap) : string option =
        if erasedTag f.Wire.Tag then
            Some f.Wire.Tag
        else
            erasedWithin f.Wire.Shape

    /// Is `after` `before` with one or more `int` positions widened to `float`, and
    /// nothing else moved (Phase 252)? Read on the wire-type objects, so a list, a
    /// map value or a union argument widens the same way a bare field does.
    let rec private widensIntToFloat (before: JVal) (after: JVal) : bool option =
        // `Some true` — widened somewhere; `Some false` — identical; `None` — any
        // other difference.
        match before, after with
        | JObj bs, JObj afs when
            (List.tryPick (fun (k, v) -> if k = "$type" then Some v else None) bs = Some(JStr "int")
             && List.tryPick (fun (k, v) -> if k = "$type" then Some v else None) afs = Some(JStr "float")
             && bs.Length = 1
             && afs.Length = 1)
            ->
            Some true
        | JObj bs, JObj afs when List.map fst bs = List.map fst afs ->
            (Some false, List.zip bs afs)
            ||> List.fold (fun acc ((_, bv), (_, av)) ->
                match acc, widensIntToFloat bv av with
                | None, _
                | _, None -> None
                | Some x, Some y -> Some(x || y))
        | JArr bs, JArr afs when bs.Length = afs.Length ->
            (Some false, List.zip bs afs)
            ||> List.fold (fun acc (bv, av) ->
                match acc, widensIntToFloat bv av with
                | None, _
                | _, None -> None
                | Some x, Some y -> Some(x || y))
        | b, a when b = a -> Some false
        | _ -> None

    /// A field's wire type widened from `int` to `float`, somewhere in it, and moved nowhere else.
    let private isIntToFloatWidening (before: FieldSnap) (after: FieldSnap) : bool =
        widensIntToFloat before.Wire.Shape after.Wire.Shape = Some true

    /// A host-surface block that declares a hosted codec (an `encode` or a `decode` member).
    let private isHostedBlock (block: JVal) =
        (Decoder.tryMember "encode" block).IsSome
        || (Decoder.tryMember "decode" block).IsSome

    /// A HOSTED slot's declaration moved: its host type or its codec.
    let private hostedSlotMoved (before: HostSurface) (after: HostSurface) : bool =
        let hosted (h: HostSurface) =
            h.Blocks |> List.filter (snd >> isHostedBlock) |> Map.ofList

        hosted before <> hosted after

    /// The F# host TYPE a host-surface block declares moved — the one member the generated record
    /// spells.
    let private hostTypeMoved (before: HostSurface) (after: HostSurface) : bool =
        let types (h: HostSurface) =
            h.Blocks
            |> List.map (fun (path, b) -> path, (Decoder.optField "fsharp" Decoder.str b |> Result.defaultValue None))
            |> Map.ofList

        types before <> types after

    /// A field added to an existing owner. The optionality class decides
    /// everything, and the class is a fact about what an OLD document does under
    /// the new vocabulary (Phase 304): `required` is the one every stored document
    /// of the owner fails, and it is the one most likely to be declared additive by
    /// hand.
    let private classifyFieldAdd (owner: Owner) (f: FieldSnap) =
        match f.Presence with
        | PresenceSnap.Required ->
            // Phase 304 — this row was `BreakingForEmitters` (a minor) on the claim that
            // old documents still decode. They do not: the decoder refuses a document
            // missing a required member ("required field '<name>' is absent"), and an
            // authoring default never fills on decode. A minor would let a `Behind`
            // consumer tolerate the profile and then refuse every stored document.
            BreakingWire,
            sprintf
                "a REQUIRED field added to %s — every document written under the previous contract lacks it, and the decoder refuses a document missing a required member (an authoring default is applied by the smart constructors, never on decode). Old documents stop decoding, so this is a `/v2/` event, not a minor; declare an optional or omit-at-default field if old documents must survive."
                owner.Describe,
            "docs/idl-stability-classes.md (a class is what an old document does under the new vocabulary, Phase 304); Idl.Decode (a required member absent is refused)"
        | PresenceSnap.HostOnly ->
            HostSurfaceOnly,
            sprintf
                "a host-only field added to %s — never on the wire in either direction (WIRE_FORMAT.md §9), so no document changes."
                owner.Describe,
            "WIRE_FORMAT.md §9 (wire-omitted fields by design)"
        | _ ->
            Additive,
            sprintf
                "an %s field added to %s — omitted when absent, so every existing document is byte-unchanged and stays valid."
                f.Presence.Class
                owner.Describe,
            "STABILITY.md: \"a new optional field that is omitted when absent\" is non-breaking"

    /// A declared annotation set moved (Phase 113). Never a wire event in either
    /// direction — the codec does not read annotations, so every document's bytes
    /// are identical either side of any change here.
    ///
    /// **Two verdicts, and the split is the point of the annotation set.** MARKING a
    /// member is `Additive`: nothing that was valid stops being valid, no emitter
    /// that conformed stops conforming, and the generated declaration gains a doc
    /// comment and a warning-grade `Obsolete` attribute that a consumer chooses what
    /// to do about. That is what lets a vocabulary retire a case across two releases
    /// — mark it in one, remove it in the next — without the MARKING itself costing
    /// a breaking bump, which is the retirement path the vocabulary-growth charter
    /// otherwise has no room for.
    ///
    /// Every other move — changing a marking, or withdrawing one — is
    /// `HostSurfaceOnly`: still nothing on the wire, but the generated declaration
    /// moved, which is a recompile event for the reference host (an `Obsolete`
    /// attribute appearing or vanishing changes which warnings a consumer sees) and
    /// invisible to every third-party codec. Withdrawing a `deprecated` is the case
    /// worth naming: an un-retirement is a plain change, not a breaking one.
    let private classifyAnnotations (subject: string) (before: string) (after: string) =
        if before = "" then
            Additive,
            sprintf
                "%s gained declared annotations. An annotation is never on the wire — the codec does not read it — so every existing document is byte-unchanged and every conformant emitter stays conformant. Marking a member is what makes a two-release retirement possible without a breaking bump for the marking itself."
                subject,
            "Idl.Annotations (Phase 113); VOCABULARY.md §4.1 (additive)"
        elif after = "" then
            HostSurfaceOnly,
            sprintf
                "%s had its declared annotations WITHDRAWN. Still nothing on the wire, but the generated declaration moved: the doc comment and any `System.Obsolete` attribute are gone, so a consumer that was being warned no longer is. A plain change — an un-retirement is not a breaking event."
                subject,
            "Idl.Annotations (Phase 113); WIRE_FORMAT.md §13 by the same argument"
        else
            HostSurfaceOnly,
            sprintf
                "%s changed its declared annotations. Nothing on the wire; the generated declaration's doc comment and `System.Obsolete` message moved. A recompile event for the reference host, invisible to every third-party codec."
                subject,
            "Idl.Annotations (Phase 113); WIRE_FORMAT.md §13 by the same argument"

    let private row (subject: string) (wire: string) (fsharp: string) =
        { Subject = subject
          Wire = wire
          FSharp = fsharp }

    let private always (cs: FSharpConsequence list) : Context -> FSharpConsequence list = fun _ -> cs

    /// Phase 539 — an alias list's verdict: `Additive` when every spelling the old revision accepted
    /// is still accepted (a decoder reads strictly more documents), a wire break when one is
    /// withdrawn (a document in that spelling stops reading). Neither moves a generated declaration.
    let private classifyAliases (subject: string) (before: string list) (after: string list) =
        match before |> List.filter (fun a -> not (List.contains a after)) with
        | [] ->
            Additive,
            sprintf
                "%s accepts more spellings (%s → %s); every document that read still reads."
                subject
                (String.concat ", " before)
                (String.concat ", " after),
            "DECISIONS.md D190 (an alias is decode-only: accepted and normalised, never encoded)"
        | withdrawn ->
            BreakingWire,
            sprintf
                "%s withdrew the alias(es) %s — a document written in a withdrawn spelling no longer reads."
                subject
                (String.concat ", " withdrawn),
            "DECISIONS.md D190 (an alias is decode-only: accepted and normalised, never encoded)"

    /// The alias rows share one verdict and one documented shape.
    let private aliasRow (subject: string) =
        row subject "`additive` when no alias is withdrawn, `breaking-wire` otherwise" "none"

    /// The annotation rows share one verdict and one documented shape.
    let private annotationRow (subject: string) =
        row
            subject
            "`additive` on a first marking, `host-surface-only` otherwise"
            "`no-generated-shape-change` — an `Obsolete` attribute moves, which changes which **warnings** a consumer sees, not a shape"

    /// A `mk<Kind>` smart constructor takes a parameter for every REQUIRED field with no
    /// authoring default (`Gen.fsharpModuleWith`'s `defaultsDecl`), so a default added to or
    /// removed from such a field moves the constructor's parameter list — a construction break
    /// at every call site. On any other optionality the constructor's BODY moves and its shape
    /// does not. Without a snapshot to ask (the context-free `consequences`), the field is read
    /// as required: the answer that costs a consumer a rebuild rather than a surprise.
    let private defaultConsequence (ctx: Context) (kind: string) (field: string) =
        match ctx.Presence kind field with
        | Some PresenceSnap.Required
        | None -> construction
        | Some _ -> noShape

    /// The same parameter rule from the OPTIONALITY side: a kind field moving into or out of
    /// `required` (with no authoring default to stand in for the parameter) is a parameter
    /// arriving at or leaving `mk<Kind>`, whichever class is on the other side. Found by the
    /// consequence property (Phase 293): `required -> omitDefault` emits the same record member
    /// and a shorter constructor, which the axis used to read as no shape change.
    let private requiredFlipped (ctx: Context) (owner: Owner) (field: string) (before: FieldSnap) (after: FieldSnap) =
        match owner with
        | OKind tag ->
            (before.Presence = PresenceSnap.Required)
            <> (after.Presence = PresenceSnap.Required)
            && not (ctx.HasDefault tag field)
        | _ -> false

    /// Phase 524 — THE rule for a change: one total match over `Change`, its arms in rank order.
    /// Totality is the compiler's (an incomplete match is an error on this side), where the table
    /// it replaces was thirty-six closures joined to the union by reflection and matched again
    /// inside every closure.
    let ruleOf (c: Change) : Rule =
        match c with
        | ArtifactVersionChanged(b, a) ->
            rule
                "ArtifactVersionChanged"
                "00"
                NoFamily
                ""
                (HostSurfaceOnly,
                 sprintf
                     "the artifact ENCODING version moved %d → %d. This describes the shape of idl.json itself, not the vocabulary it carries — reconcile the two revisions' encodings before trusting any other row."
                     b
                     a,
                 "Artifact.version")
                (always noShape)
                (sprintf "artifact encoding version %d -> %d" b a)
                [ row "the artifact's own encoding version" "`host-surface-only`" "`no-generated-shape-change`" ]

        | WireShapeChanged(b, a) ->
            rule
                "WireShapeChanged"
                "01"
                NoFamily
                ""
                (BreakingWire,
                 sprintf
                     "the declared WIRE SHAPE moved %s → %s — the discriminator key, the node-envelope nesting and/or the canonical key order relocate every tag or every byte on the wire, so every document's bytes move. A `/v2/` major event by definition."
                     b
                     a,
                 "Idl.WireShape (Phases 108/109/111); VOCABULARY.md §4.2")
                (always noShape)
                (sprintf "wire shape changed: %s -> %s" b a)
                [ row "the declared wire shape" "`breaking-wire`" "`no-generated-shape-change`" ]

        | HardenPolicyChanged(b, a) ->
            rule
                "HardenPolicyChanged"
                "02"
                NoFamily
                ""
                (HostSurfaceOnly,
                 sprintf
                     "the declared HARDENING vocabulary moved %s → %s — the codegen trust boundary now gates a different kind, or mints a different placeholder, or matches a different literal case. Nothing here moves a document's bytes BY ITSELF: the one wire-visible member is the transparent-case set, whose effect is reported per union as its own row. What changes is what SCAFFOLDED source contains, so re-scaffold anything generated against the old declaration."
                     b
                     a,
                 "Idl.HardenPolicy (Phase 116); STABILITY.md the IDL engine")
                (always noShape)
                (sprintf "harden policy changed: %s -> %s" b a)
                [ row "the hardening vocabulary" "`host-surface-only`" "`no-generated-shape-change`" ]

        | KindAdded t ->
            rule
                "KindAdded"
                "10"
                KindSet
                (t)
                (Additive,
                 sprintf
                     "kind `%s` added — a new `$type` branch on the schema's top-level `oneOf`; every previously-valid document stays valid."
                     t,
                 "VOCABULARY.md §4.1 (additive `core@1.(x+1)` profile minor); STABILITY.md (NodeKind addition = minor)")
                (always matching)
                (sprintf "kind added: %s" t)
                [ row
                      "a node kind added"
                      "`additive`"
                      "`exhaustive-match` (a kind is a case of the generated node-kind DU)" ]

        | KindRemoved t ->
            rule
                "KindRemoved"
                "11"
                KindSet
                (t)
                (BreakingWire,
                 sprintf
                     "kind `%s` REMOVED — retiring a `$type` discriminator invalidates every document that used it. A `/v2/` major event, and per §4.2 a thing to do before publication or not at all."
                     t,
                 "VOCABULARY.md §4.2 (removal / rename = a `v2` major)")
                (always matching)
                (sprintf "kind removed: %s" t)
                [ row "a node kind removed" "`breaking-wire`" "`exhaustive-match`" ]

        | KindRenamed(o, n) ->
            rule
                "KindRenamed"
                "12"
                KindSet
                (o + ">" + n)
                (BreakingWire,
                 sprintf
                     "INFERRED rename `%s` → `%s` (identical field signature, unique on both sides). Inference, not a declaration — the wire records no identity beyond the `$type` string. The add + remove above stand on their own; this row only explains them."
                     o
                     n,
                 "VOCABULARY.md §4.2 (a `$type` rename is a breaking wire change)")
                // Reported ALONGSIDE the add + remove that explain it, and those two rows carry
                // the consequence; claiming it again here would double-count.
                (always noShape)
                (sprintf "kind renamed (inferred): %s -> %s" o n)
                [ row
                      "a node kind renamed (inferred, reported beside the add and the remove it explains)"
                      "`breaking-wire`"
                      "`no-generated-shape-change` — the add and the remove carry the consequence" ]

        | KindCategoryChanged(t, b, a) ->
            rule
                "KindCategoryChanged"
                "13"
                NoFamily
                (t)
                (HostSurfaceOnly,
                 sprintf
                     "kind `%s` re-categorised %s → %s. `Category` is metadata and is never serialised (Idl.IdlKind) — no document changes."
                     t
                     b
                     a,
                 "Idl.IdlKind (`Category` is metadata, not serialised)")
                (always noShape)
                (sprintf "kind %s category %s -> %s" t b a)
                [ row "a kind's category" "`host-surface-only`" "`no-generated-shape-change`" ]

        | KindAnnotationsChanged(o, b, a) ->
            rule
                "KindAnnotationsChanged"
                "14"
                NoFamily
                (o.Key)
                (classifyAnnotations o.Describe b a)
                // Phase 119 — the same three grades, for the same reason: a kind-level or
                // enum-case marking is no more on the wire than a field's, so marking a whole
                // node kind for retirement costs no breaking bump and the two-release
                // retirement path the charter needs is affordable.
                (always noShape)
                (sprintf "annotations %s: %s" (if b = "" then "declared" else "changed") o.Describe)
                [ annotationRow "a kind's or a tree-op's annotation set" ]

        | OpAdded t ->
            rule
                "OpAdded"
                "20"
                DiscriminatorFamily
                (t)
                (Additive,
                 sprintf "op `%s` added — a new `$type` branch on the TreeOp union; existing op streams stay valid." t,
                 "WIRE_FORMAT.md §3.4; VOCABULARY.md §4.1 by the same additive argument")
                // The op vocabulary has no generated F# shape at all — the F# type emitter leaves
                // that leg unshipped (Phase 703) — so an op change moves no declaration. This row
                // changes the day that leg lands.
                (always noShape)
                (sprintf "op added: %s" t)
                [ row
                      "a tree-op added"
                      "`additive`"
                      "`no-generated-shape-change` — the F# type emitter leaves the op vocabulary unshipped (Phase 703). **This row changes the day that leg lands.**" ]

        | OpRemoved t ->
            rule
                "OpRemoved"
                "21"
                DiscriminatorFamily
                (t)
                (BreakingWire,
                 sprintf
                     "op `%s` REMOVED — every persisted op stream carrying it becomes undecodable, and an op stream is a hash-chained archive, not a live message. Strictly worse than retiring a kind."
                     t,
                 "VOCABULARY.md §4.2; STABILITY.md (op-stream wire shape)")
                (always noShape)
                (sprintf "op removed: %s" t)
                [ row "a tree-op removed" "`breaking-wire`" "`no-generated-shape-change` — as for a tree-op added" ]

        | UnionAdded n ->
            rule
                "UnionAdded"
                "30"
                NoFamily
                (n)
                (Additive, sprintf "value-union `%s` introduced — reachable only from a field that also changed." n, "—")
                // A type arriving breaks nothing: no source could have named it.
                (always noShape)
                (sprintf "union added: %s" n)
                [ row "a union added" "`additive`" "`no-generated-shape-change` — nothing could have referenced it" ]

        | UnionRemoved n ->
            rule
                "UnionRemoved"
                "31"
                NoFamily
                (n)
                (BreakingWire,
                 sprintf "value-union `%s` removed — every document carrying one of its cases is invalidated." n,
                 "VOCABULARY.md §4.2")
                (always reference)
                (sprintf "union removed: %s" n)
                [ row "a union removed" "`breaking-wire`" "`type-name-reference`" ]

        | UnionCaseAdded(u, c) ->
            rule
                "UnionCaseAdded"
                "32"
                DiscriminatorFamily
                (u + "." + c)
                (Additive,
                 sprintf
                     "case `%s` added to `%s` — a `$type`-discriminator family (WIRE_FORMAT.md §11), so the wire-coupling cost is IDENTICAL to a new kind's; only the confusion cost is smaller. Governed: it still cites §1.1 demand evidence and acknowledges the §11 cost."
                     c
                     u,
                 "VOCABULARY.md §2 (the quiet-churn caveat); WIRE_FORMAT.md §11 (discriminator families)")
                (always matching)
                (sprintf "union case added: %s.%s" u c)
                [ row "a union case added" "`additive`" "`exhaustive-match`" ]

        | UnionCaseRemoved(u, c) ->
            rule
                "UnionCaseRemoved"
                "33"
                DiscriminatorFamily
                (u + "." + c)
                (BreakingWire,
                 sprintf "case `%s` REMOVED from `%s` — a retired `$type` in a discriminator family." c u,
                 "VOCABULARY.md §4.2")
                (always matching)
                (sprintf "union case removed: %s.%s" u c)
                [ row "a union case removed" "`breaking-wire`" "`exhaustive-match`" ]

        | UnionParamsChanged(n, b, a) ->
            rule
                "UnionParamsChanged"
                "34"
                NoFamily
                (n)
                (HostSurfaceOnly,
                 sprintf
                     "`%s` type parameters %A → %A — generic arity is a host-declaration property; the wire carries no type arguments."
                     n
                     b
                     a,
                 "Idl.IdlUnion.Params")
                (always reference)
                (sprintf "union %s type parameters changed" n)
                // The document used to say `breaking-wire` here (Phase 293 corrected it to what the
                // classifier has always decided): the wire carries no type arguments.
                [ row
                      "a union's type parameters moved"
                      "`host-surface-only` — the wire carries no type arguments"
                      "`type-name-reference` (the wrong arity)" ]

        | UnionTransparencyChanged(n, b, a) ->
            rule
                "UnionTransparencyChanged"
                "35"
                DiscriminatorFamily
                (n)
                (BreakingWire,
                 sprintf
                     "`%s` transparent case %A → %A — a transparent case encodes as a BARE value rather than a `$type`-tagged object, so this moves the bytes of every document using it."
                     n
                     b
                     a,
                 "Idl.TransparentUnion; STABILITY.md wire-format section")
                (always noShape)
                (sprintf "union %s transparent case changed" n)
                [ row "a union's transparent case" "`breaking-wire`" "`no-generated-shape-change`" ]

        | UnionTagRuleChanged(n, b, a) ->
            rule
                "UnionTagRuleChanged"
                "36"
                DiscriminatorFamily
                n
                (BreakingWire,
                 sprintf
                     "`%s` tag-naming rule %s → %s — every case's `$type` is derived from its name by the rule, so this re-tags every document carrying the union."
                     n
                     b
                     a,
                 "Idl.UnionTagRule (Phase 550); DECISIONS.md D163")
                (always noShape)
                (sprintf "union %s tag rule changed" n)
                [ row "a union's tag-naming rule" "`breaking-wire`" "`no-generated-shape-change`" ]

        | UnionCaseHostedChanged(u, c, b, a) ->
            rule
                "UnionCaseHostedChanged"
                "37"
                DiscriminatorFamily
                (u + "." + c)
                (Unclassifiable,
                 sprintf
                     "case `%s` of `%s` changed its case-level host codec. The case's fields still state its wire members, but the F# host reads and writes them through the codec, which the artifact does not show. CHECK: does every document the old codec wrote decode under the new one and come back byte-identical? If so, this is host-surface only; if not, it is BREAKING (wire)."
                     c
                     u,
                 "Idl.IdlUnionCase.Hosted (Phase 550); docs/idl-stability-classes.md (anything crossing an erased slot)")
                (always construction)
                (sprintf "union case host codec changed: %s.%s" u c)
                [ row
                      "a union case's case-level host codec"
                      "`undecided`"
                      "`full-literal-construction` — the host case's payload is the codec's" ]

        | EnumAdded n ->
            rule
                "EnumAdded"
                "40"
                NoFamily
                (n)
                (Additive, sprintf "closed set `%s` introduced." n, "—")
                (always noShape)
                (sprintf "closed set added: %s" n)
                [ row "an enum added" "`additive`" "`no-generated-shape-change`" ]

        | EnumRemoved n ->
            rule
                "EnumRemoved"
                "41"
                NoFamily
                (n)
                (BreakingWire, sprintf "closed set `%s` removed — its field must have changed type or gone." n, "—")
                (always reference)
                (sprintf "closed set removed: %s" n)
                [ row "an enum removed" "`breaking-wire`" "`type-name-reference`" ]

        | EnumCaseAdded(e, w) ->
            rule
                "EnumCaseAdded"
                "42"
                EnumSet
                (e + "." + w)
                (Additive,
                 sprintf
                     "wire string `\"%s\"` added to closed set `%s` — additive on the wire, but a decoder that predates it REJECTS the value (`UNKNOWN_DU_CASE`), so the host-lag commitment applies exactly as it does to a kind."
                     w
                     e,
                 "VOCABULARY.md §4.3 (unknown-discriminator behaviour + host-lag)")
                (always matching)
                (sprintf "enum case added: %s.\"%s\"" e w)
                [ row "an enum case added" "`additive`" "`exhaustive-match`" ]

        | EnumCaseRemoved(e, w) ->
            rule
                "EnumCaseRemoved"
                "43"
                EnumSet
                (e + "." + w)
                (BreakingWire,
                 sprintf
                     "wire string `\"%s\"` REMOVED from closed set `%s` — documents carrying it no longer validate."
                     w
                     e,
                 "VOCABULARY.md §4.2")
                (always matching)
                (sprintf "enum case removed: %s.\"%s\"" e w)
                [ row "an enum case removed" "`breaking-wire`" "`exhaustive-match`" ]

        | EnumHostMappingChanged(n, _, _) ->
            rule
                "EnumHostMappingChanged"
                "44"
                NoFamily
                (n)
                (HostSurfaceOnly,
                 sprintf
                     "`%s` host case names changed with its wire strings unchanged — `hostCases` is a hostSurface key (WIRE_FORMAT.md §13), carrying nothing observable on the wire. A source-compat event for F# consumers, not a wire one."
                     n,
                 "WIRE_FORMAT.md §13; Artifact.json (`hostCases` is hostSurface)")
                // The host-side case NAMES moved: every arm that spelled one is now undefined,
                // and the set is no longer covered.
                (always matching)
                (sprintf "enum %s host case names changed" n)
                [ row "an enum's **host** case names moved" "`host-surface-only`" "`exhaustive-match`" ]

        | EnumCaseAnnotationsChanged(e, w, b, a) ->
            rule
                "EnumCaseAnnotationsChanged"
                "45"
                NoFamily
                (e + "." + w)
                (classifyAnnotations (sprintf "case `\"%s\"` of enum `%s`" w e) b a)
                (always noShape)
                (sprintf "enum case annotations %s: %s.\"%s\"" (if b = "" then "declared" else "changed") e w)
                [ annotationRow "an enum case's annotation set" ]

        | EnumCaseAliasesChanged(e, w, b, a) ->
            rule
                "EnumCaseAliasesChanged"
                "46"
                NoFamily
                (e + "." + w)
                (classifyAliases (sprintf "case `\"%s\"` of enum `%s`" w e) b a)
                (always noShape)
                (sprintf
                    "enum case aliases changed: %s.\"%s\" : [%s] -> [%s]"
                    e
                    w
                    (String.concat ", " b)
                    (String.concat ", " a))
                [ aliasRow "an enum case's alias list" ]

        | RecordAdded n ->
            rule
                "RecordAdded"
                "50"
                NoFamily
                (n)
                (Additive,
                 sprintf "non-discriminated record `%s` introduced — reachable only from a field that also changed." n,
                 "—")
                (always noShape)
                (sprintf "record added: %s" n)
                [ row "a record added" "`additive`" "`no-generated-shape-change`" ]

        | RecordRemoved n ->
            rule
                "RecordRemoved"
                "51"
                NoFamily
                (n)
                (BreakingWire, sprintf "record `%s` removed." n, "VOCABULARY.md §4.2")
                (always reference)
                (sprintf "record removed: %s" n)
                [ row "a record removed" "`breaking-wire`" "`type-name-reference`" ]

        | FieldAdded(o, f) ->
            rule
                "FieldAdded"
                "60"
                NoFamily
                (o.Key + "/" + f.Name)
                (classifyFieldAdd o f)
                // A field on any owner is a record member.
                (always construction)
                (sprintf "field added: %s.%s : %s (%s)" o.Describe f.Name f.Label f.Presence.Class)
                [ row
                      "a **required** field added — every old document lacks it and is refused"
                      "`breaking-wire`"
                      "`full-literal-construction`"
                  row "an **optional** field added" "`additive`" "`full-literal-construction`"
                  row "a **host-only** field added" "`host-surface-only`" "`full-literal-construction`" ]

        | FieldRemoved(o, n, was) ->
            rule
                "FieldRemoved"
                "61"
                NoFamily
                (o.Key + "/" + n)
                ((match was.Presence with
                  | PresenceSnap.HostOnly ->
                      HostSurfaceOnly,
                      sprintf
                          "host-only field `%s` removed from %s — never on the wire, so no document changes."
                          n
                          o.Describe,
                      "WIRE_FORMAT.md §9"
                  | _ ->
                      BreakingWire,
                      sprintf
                          "field `%s` REMOVED from %s — a slot that was on the wire is gone. Decoders that read it break; emitters that write it produce an unknown key."
                          n
                          o.Describe,
                      "STABILITY.md wire-format section (removal is a major event)"))
                (always construction)
                (sprintf "field removed: %s.%s" o.Describe n)
                [ row
                      "a field removed"
                      "`breaking-wire`; `host-surface-only` for a host-only field"
                      "`full-literal-construction`" ]

        | FieldTypeChanged(o, n, b, a) ->
            rule
                "FieldTypeChanged"
                "62"
                NoFamily
                (o.Key + "/" + n)
                (match erasedIn b |> Option.orElse (erasedIn a) with
                 | Some tag ->
                     Unclassifiable,
                     sprintf
                         "field `%s` on %s changed type across an ERASED slot: %s → %s. The artifact does not state what a `%s` slot admits — that is the host codec's business, by design — so nothing here can say whether the admitted value sets differ, and a nested erased slot (a list element, a map value, a union argument) is as undecidable as a bare one. CHECK: does every value the old side accepted still decode, and does the corpus come back byte-identical? If both, this is a modelling improvement and not a wire event; if either fails, it is BREAKING (wire)."
                         n
                         o.Describe
                         b.Label
                         a.Label
                         tag,
                     "Idl.THosted / TJson / TOpaque (content carried verbatim; not described by the artifact)"
                 | None when isIntToFloatWidening b a ->
                     // Phase 252 — a float slot admits every integer literal and a whole float
                     // renders as the same digits, so every old document decodes and re-encodes
                     // byte-identically, and an old emitter (writing integers) stays conformant.
                     // That is the table's definition of additive; the cost is host lag, as for
                     // a new enum case: a decoder that predates the widening refuses `2.5`.
                     Additive,
                     sprintf
                         "field `%s` on %s WIDENED: %s → %s. A float slot admits every integer, and a whole float renders as the same digits, so every existing document decodes and re-encodes byte-identically and every previously-conformant emitter stays conformant. Not breaking-for-emitters: no old emitter's output became invalid. The cost is host lag — a decoder that predates the widening refuses a fractional value such as `2.5` — exactly as for a new enum case."
                         n
                         o.Describe
                         b.Label
                         a.Label,
                     "docs/idl-stability-classes.md (int → float widening, Phase 252); VOCABULARY.md §4.3 (host-lag)"
                 | None ->
                     BreakingWire,
                     sprintf
                         "field `%s` on %s changed type: %s → %s. A value that decoded no longer does."
                         n
                         o.Describe
                         b.Label
                         a.Label,
                     "STABILITY.md wire-format section")
                (fun _ ->
                    if (erasedIn b).IsSome || (erasedIn a).IsSome then
                        [ GeneratedShapeUnreadable ]
                    else
                        construction)
                (sprintf "field type changed: %s.%s : %s -> %s" o.Describe n b.Label a.Label)
                [ row "a field's type moved" "`breaking-wire`" "`full-literal-construction`"
                  row
                      "a field's type **widened from `int` to `float`** (anywhere in it — a list element, a map value, a union argument)"
                      "`additive`"
                      "`full-literal-construction`"
                  row
                      "a field's type moved across an **erased** slot (`hosted` / `json` / `opaque`), at any depth — a list element, a map value, a union argument"
                      "`undecided`"
                      "`generated-shape-unreadable`" ]

        | FieldOptionalityChanged(o, n, b, a) ->
            rule
                "FieldOptionalityChanged"
                "63"
                NoFamily
                (o.Key + "/" + n)
                (let d = o.Describe

                 (match b.Presence, a.Presence with
                  // Phase 304 — tightening was `BreakingForEmitters` (a minor) and loosening
                  // `BreakingWire` (a major): the inverse of what old documents do. A class is a
                  // fact about an old document under the new vocabulary, never about the emitter
                  // alone.
                  | PresenceSnap.Optional, PresenceSnap.Required
                  | PresenceSnap.OmitDefault _, PresenceSnap.Required ->
                      BreakingWire,
                      sprintf
                          "field `%s` on %s became REQUIRED (%s → %s) — every stored document that omitted it (absent, or sitting on its default) is now refused by the decoder, and every emitter that legitimately omitted it produces a refused document. Old documents stop decoding: a `/v2/` event."
                          n
                          d
                          b.Presence.Label
                          a.Presence.Label,
                      "docs/idl-stability-classes.md (a class is what an old document does, Phase 304); Idl.Decode (a required member absent is refused)"
                  | PresenceSnap.Required, PresenceSnap.Optional ->
                      // Loosening. Every old document carries the member, decodes to the same
                      // value and re-encodes to the same bytes; every old emitter writes it, so
                      // stays conformant — the table's definition of additive. The consumer that
                      // relied on presence meets absence only in a document a NEW emitter writes,
                      // which is host lag (a decoder that predates the change refuses it), exactly
                      // as for a new enum case; on the F# axis the member becomes an `option`, which
                      // `full-literal-construction` carries.
                      Additive,
                      sprintf
                          "field `%s` on %s stopped being required (%s → %s) — every existing document carries it, decodes to the same value and re-encodes byte-identically, and every previously-conformant emitter (which always writes it) stays conformant. The cost is host lag: a consumer that relied on presence meets absence only in documents a NEW emitter writes, and a decoder that predates the change refuses those — as for a new enum case."
                          n
                          d
                          b.Presence.Label
                          a.Presence.Label,
                      "docs/idl-stability-classes.md (loosening, Phase 304); VOCABULARY.md §4.3 (host-lag)"
                  | PresenceSnap.Required, PresenceSnap.OmitDefault _ ->
                      // Loosening to omit-at-default is NOT additive: the decoder reads a member
                      // sitting on the default and the encoder then omits it, so every stored
                      // document carrying the default value changes bytes on re-encode — the
                      // argument the moved-identity-default row already rests on.
                      BreakingWire,
                      sprintf
                          "field `%s` on %s stopped being required and became omit-at-default (%s → %s) — every old document still decodes to the same value, but one carrying the default re-encodes WITHOUT the member, so its bytes move: omit-at-default is wire-visible. A hash-chained store re-encoding an old document would not reproduce it."
                          n
                          d
                          b.Presence.Label
                          a.Presence.Label,
                      "Idl.Optionality.OmitDefault (omit-at-default is wire-visible); docs/idl-stability-classes.md (Phase 304)"
                  | PresenceSnap.OmitDefault _, PresenceSnap.OmitDefault _ ->
                      BreakingWire,
                      sprintf
                          "field `%s` on %s moved its identity default (%s → %s) — omit-at-default is WIRE-VISIBLE: every document sitting on the old default changes bytes, and every document carrying the new one loses a key. The single most easily mis-declared change in this table."
                          n
                          d
                          b.Presence.Label
                          a.Presence.Label,
                      "Idl.Optionality.OmitDefault (omit-at-default is wire-visible)"
                  | PresenceSnap.HostOnly, _
                  | _, PresenceSnap.HostOnly ->
                      BreakingWire,
                      sprintf
                          "field `%s` on %s crossed the host-only boundary (%s → %s) — a slot appeared on, or vanished from, the wire."
                          n
                          d
                          b.Presence.Label
                          a.Presence.Label,
                      "WIRE_FORMAT.md §9"
                  | _ ->
                      BreakingWire,
                      sprintf "field `%s` on %s changed optionality (%s → %s)." n d b.Presence.Label a.Presence.Label,
                      "STABILITY.md wire-format section"))
                // Only the `optional` class emits an F# `option`, so the generated type moves
                // exactly when one side is optional and the other is not. Every other optionality
                // move (required <-> omitDefault) changes the ENCODER body and leaves the record
                // member's type where it was — decidable, so decided, rather than reported
                // conservatively.
                (fun ctx ->
                    if
                        (b.Presence = PresenceSnap.Optional) <> (a.Presence = PresenceSnap.Optional)
                        || requiredFlipped ctx o n b a
                    then
                        construction
                    else
                        noShape)
                (sprintf "field optionality changed: %s.%s : %s -> %s" o.Describe n b.Presence.Label a.Presence.Label)
                // Each row names what an OLD document does under the new vocabulary (Phase 304):
                // tightening to `required` refuses the documents that omitted the member; loosening
                // to `optional` leaves every one decoding and re-encoding identically; loosening to
                // `omitDefault`, like a moved identity default, moves the bytes of every document
                // sitting on the default.
                [ row
                      "a field's optionality moved **into or out of** `optional`"
                      "`breaking-wire` when it became required (old documents that omitted it are refused); `additive` when `required` became `optional` (every old document carries it and re-encodes identically; the consumer that relied on presence meets absence only from a new emitter — host lag); `breaking-wire` between `optional` and `omitDefault` (absence changes meaning, or a default-valued member stops re-encoding)"
                      "`full-literal-construction`"
                  row
                      "a field's optionality moved **between** `required` and `omitDefault`, or its identity default moved"
                      "`breaking-wire` — becoming required refuses the old documents that omitted it; becoming omit-at-default, or moving the default, re-encodes every old document sitting on the default without the member (omit-at-default is wire-visible)"
                      "`full-literal-construction` on a kind field with no authoring default — `mk<Kind>` takes a parameter for every required field, so the parameter leaves or arrives — else `no-generated-shape-change`"
                  row
                      "a field crossed the **host-only** boundary"
                      "`breaking-wire`"
                      "`full-literal-construction` when it crossed `optional` too, else `no-generated-shape-change`" ]

        | FieldHostSurfaceChanged(o, n, before, after) ->
            rule
                "FieldHostSurfaceChanged"
                "64"
                NoFamily
                (o.Key + "/" + n)
                (if hostedSlotMoved before after then
                     Unclassifiable,
                     sprintf
                         "field `%s` on %s changed a HOSTED slot's declaration (its host type or its codec). A hosted slot's codec IS its wire form, and the artifact does not state what the codec writes, so nothing here can say whether a document's bytes moved. CHECK: does every value the old codec wrote still decode under the new one, and does every document come back byte-identical? If both, this is host-surface only; if either fails, it is BREAKING (wire)."
                         n
                         o.Describe,
                     "Idl.THosted (the codec is the wire form; Phase 252); docs/idl-stability-classes.md (anything crossing an erased slot)"
                 else
                     HostSurfaceOnly,
                     sprintf
                         "field `%s` on %s changed its hostSurface declaration only — the generated F#/TS signature moved, the wire did not. A recompile event for the reference host; invisible to every third-party codec."
                         n
                         o.Describe,
                     "WIRE_FORMAT.md §13 (hostSurface is host-language spec, not wire spec)")
                // The generated DECLARATION is readable whatever the wire verdict: a `hostSurface`
                // block states the F# host type outright (Phase 252). Only the `fsharp` member is
                // spelled by the generated record, so a codec or placeholder expression moving
                // alone leaves every site where it was — decidable, so decided, including on a
                // hosted slot whose WIRE verdict is undecided.
                (fun _ -> if hostTypeMoved before after then construction else noShape)
                (sprintf "field hostSurface changed: %s.%s" o.Describe n)
                [ row
                      "a `fn` slot's `hostSurface` block moved"
                      "`host-surface-only`"
                      "`full-literal-construction` when its `fsharp` signature moved, else `no-generated-shape-change`"
                  row
                      "a **hosted** slot's `hostSurface` block moved — its host type, its `encode` or its `decode`"
                      "`undecided`"
                      "`full-literal-construction` when its `fsharp` type moved, else `no-generated-shape-change`" ]

        | FieldAnnotationsChanged(o, n, b, a) ->
            rule
                "FieldAnnotationsChanged"
                "65"
                NoFamily
                (o.Key + "/" + n)
                (classifyAnnotations (sprintf "field `%s` on %s" n o.Describe) b a)
                (always noShape)
                (sprintf "field annotations %s: %s.%s" (if b = "" then "declared" else "changed") o.Describe n)
                [ annotationRow "a field's annotation set" ]

        | UnionCaseAnnotationsChanged(u, c, b, a) ->
            rule
                "UnionCaseAnnotationsChanged"
                "66"
                NoFamily
                (u + "." + c)
                (classifyAnnotations (sprintf "case `%s` of `%s`" c u) b a)
                (always noShape)
                (sprintf "union case annotations %s: %s.%s" (if b = "" then "declared" else "changed") u c)
                [ annotationRow "a union case's annotation set" ]

        | TypeAnnotationsChanged(f, n, b, a) ->
            rule
                "TypeAnnotationsChanged"
                "67"
                NoFamily
                (f + ":" + n)
                (classifyAnnotations (sprintf "%s `%s`" f n) b a)
                // Phase 548 — the same three grades a kind's or a member's marking takes, for the
                // same reason: a type-level marking is no more on the wire than theirs, so marking
                // a whole union, record or enum for retirement costs no breaking bump.
                (always noShape)
                (sprintf "type annotations %s: %s %s" (if b = "" then "declared" else "changed") f n)
                [ annotationRow "a union's, a record's or an enum's own annotation set" ]

        | FieldAliasesChanged(o, n, b, a) ->
            rule
                "FieldAliasesChanged"
                "68"
                NoFamily
                (o.Key + "/" + n)
                (classifyAliases (sprintf "field `%s` on %s" n o.Describe) b a)
                (always noShape)
                (sprintf
                    "field aliases changed: %s.%s : [%s] -> [%s]"
                    o.Describe
                    n
                    (String.concat ", " b)
                    (String.concat ", " a))
                [ aliasRow "a field's alias list" ]

        | FieldPresenceChanged(o, n, b, a) ->
            let shown s = if s = "" then "none" else s

            rule
                "FieldPresenceChanged"
                "69"
                NoFamily
                (o.Key + "/" + n)
                (BreakingWire,
                 sprintf
                     "field `%s` on %s changed its presence condition (%s → %s) — the canonical bytes of every document in the configurations it governs move."
                     n
                     o.Describe
                     (shown b)
                     (shown a),
                 "DECISIONS.md D190 (a member whose condition fails is absent from the canonical form)")
                (always noShape)
                (sprintf "field presence changed: %s.%s : %s -> %s" o.Describe n (shown b) (shown a))
                [ row "a field's presence condition" "`breaking-wire`" "none" ]

        | DefaultAdded(kd, f, _) ->
            rule
                "DefaultAdded"
                "70"
                NoFamily
                (kd + "/" + f)
                (Additive,
                 sprintf
                     "smart-constructor default added for `%s.%s` — an AUTHORING default (Idl.IdlDefault), not the wire-visible omit-at-default. It changes what a host author gets when they say nothing; it does not change what the wire admits."
                     kd
                     f,
                 "Idl.IdlDefault (applied by the generated smart constructors)")
                (fun ctx -> defaultConsequence ctx kd f)
                (sprintf "authoring default added: %s.%s" kd f)
                [ row
                      "an authoring default added"
                      "`additive`"
                      "`full-literal-construction` when the field is required — `mk<Kind>` loses the parameter, so every call site moves — else `no-generated-shape-change`" ]

        | DefaultRemoved(kd, f, _) ->
            rule
                "DefaultRemoved"
                "71"
                NoFamily
                (kd + "/" + f)
                (BreakingForEmitters,
                 sprintf
                     "smart-constructor default REMOVED for `%s.%s` — host authoring code that relied on it now emits a different document (or fails to compile). Authoring-surface break; the wire contract is unchanged."
                     kd
                     f,
                 "Idl.IdlDefault")
                (fun ctx -> defaultConsequence ctx kd f)
                (sprintf "authoring default removed: %s.%s" kd f)
                [ row
                      "an authoring default removed"
                      "`breaking-for-emitters`"
                      "`full-literal-construction` when the field is required — `mk<Kind>` gains the parameter — else `no-generated-shape-change`" ]

        | DefaultChanged(kd, f, b, a) ->
            rule
                "DefaultChanged"
                "72"
                NoFamily
                (kd + "/" + f)
                (BreakingForEmitters,
                 sprintf
                     "smart-constructor default for `%s.%s` moved (%s → %s) — every authoring site that omitted the field now emits a different document. The wire contract is unchanged; the emitted bytes are not."
                     kd
                     f
                     b
                     a,
                 "Idl.IdlDefault")
                (always noShape)
                (sprintf "authoring default changed: %s.%s : %s -> %s" kd f b a)
                [ row
                      "an authoring default changed"
                      "`breaking-for-emitters`"
                      "`no-generated-shape-change` — the parameter list is unchanged, the constructor's body is not" ]

        | SupportChanged(k, b, a) ->
            rule
                "SupportChanged"
                "80"
                NoFamily
                (k)
                (let what =
                    match b, a with
                    | None, _ -> "ADDED"
                    | _, None -> "REMOVED"
                    | _ -> "changed"

                 HostSurfaceOnly,
                 sprintf
                     "declared support `%s` %s — a doc block, a verbatim splice, a case refine, a kind projection or the host prelude is host-language SOURCE the generator splices, never a wire fact: no document's bytes move and no emitter's output changes. What changes is the GENERATED module, so regenerate against the new support and read the F# consequence beside this row."
                     k
                     what,
                 "SupportArtifact (support.json, Phase 114); docs/idl-stability-classes.md (declared support, Phase 293)")
                // A projection supplies the kind's record, encoder, decoder and constructor
                // verbatim, and a type splice adds members to the type group: a move there moves
                // generated declarations. A doc block, a refine (the final expression of one
                // decoder arm, built from binders already read), an encoder/decoder/accessor
                // splice (bodies) and the prelude move no declaration a consumer constructs.
                (fun _ ->
                    if k.StartsWith "projection:" || k = "splice:type" then
                        construction
                    else
                        noShape)
                (sprintf
                    "declared support %s: %s"
                    (match b, a with
                     | None, _ -> "added"
                     | _, None -> "removed"
                     | _ -> "changed")
                    k)
                [ row
                      "a declared support entry (`support.json`) added, removed or changed — a doc block, a splice, a case refine, a kind projection, the host prelude"
                      "`host-surface-only` — host-language source the generator splices, never on the wire"
                      "`full-literal-construction` for a kind projection or the type splice (generated declarations move), else `no-generated-shape-change`" ]

    /// One change of every case, in rank order — what the `docs/` mapping table enumerates, since
    /// the rows a rule shows do not depend on the change's payload. `IdlDiffTests` holds the list
    /// to the union: every case exactly once.
    let exemplars: Change list =
        let field: FieldSnap =
            { Name = ""
              Label = ""
              Wire = { Tag = ""; Shape = JObj [] }
              Host = { Blocks = [] }
              Presence = PresenceSnap.Required
              Annotations = ""
              Aliases = []
              PresenceCondition = "" }

        let owner = ONodeEnvelope
        let host: HostSurface = { Blocks = [] }

        [ ArtifactVersionChanged(0, 0)
          WireShapeChanged("", "")
          HardenPolicyChanged("", "")
          KindAdded ""
          KindRemoved ""
          KindRenamed("", "")
          KindCategoryChanged("", "", "")
          KindAnnotationsChanged(owner, "", "")
          OpAdded ""
          OpRemoved ""
          UnionAdded ""
          UnionRemoved ""
          UnionCaseAdded("", "")
          UnionCaseRemoved("", "")
          UnionParamsChanged("", [], [])
          UnionTransparencyChanged("", None, None)
          UnionTagRuleChanged("", "", "")
          UnionCaseHostedChanged("", "", "", "")
          EnumAdded ""
          EnumRemoved ""
          EnumCaseAdded("", "")
          EnumCaseRemoved("", "")
          EnumHostMappingChanged("", [], [])
          EnumCaseAnnotationsChanged("", "", "", "")
          EnumCaseAliasesChanged("", "", [], [])
          RecordAdded ""
          RecordRemoved ""
          FieldAdded(owner, field)
          FieldRemoved(owner, "", field)
          FieldTypeChanged(owner, "", field, field)
          FieldOptionalityChanged(owner, "", field, field)
          FieldHostSurfaceChanged(owner, "", host, host)
          FieldAnnotationsChanged(owner, "", "", "")
          UnionCaseAnnotationsChanged("", "", "", "")
          TypeAnnotationsChanged("", "", "", "")
          FieldAliasesChanged(owner, "", [], [])
          FieldPresenceChanged(owner, "", "", "")
          DefaultAdded("", "", "")
          DefaultRemoved("", "", "")
          DefaultChanged("", "", "", "")
          SupportChanged("", None, None) ]

    /// Deterministic ordering: the rule's rank, then the change's own key, so identical inputs
    /// produce byte-identical output regardless of map enumeration order.
    let sortKey (c: Change) : string * string =
        let r = ruleOf c
        r.Rank, r.Key
