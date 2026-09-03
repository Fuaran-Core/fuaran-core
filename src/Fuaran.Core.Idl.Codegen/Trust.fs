namespace Fuaran.Core.Idl

open System

/// The codegen trust boundary over the `IdlValue` tree (Phase 321 task 2 + the
/// task-3 wiring): gate every node of the vocabulary's GATED kind against an
/// allowlist + content-hash, and run `Sanitize.*` over declared URL / markdown
/// fields, producing a hardened value that is inert-by-construction when scaffolded
/// to host source.
///
/// Since Phase 116 the vocabulary tokens the boundary addresses by name — the gated
/// kind, the placeholder it mints, the literal cases it sanitises — are read from the
/// [[HardenPolicy]] on the `Idl`, so a domain that spells them otherwise gets the same
/// floor without adopting another domain's names. The caller still owns the trust
/// decisions ([[Trust.Policy]]).
module Trust =

    /// An allowlisted foreign component: which module/component may resolve live,
    /// and the content-hash it must carry.
    type AllowEntry =
        { ModuleId: string
          ComponentId: string
          Hash: string }

    /// The caller's TRUST decisions: which foreign components may resolve live, and
    /// which of the vocabulary's fields carry values that must be sanitised at codegen
    /// time. A URL field's value is the literal case of a binding-shaped union; a
    /// markdown field's is the literal case of a text-shaped union. Keyed by
    /// `(kindTag, fieldName)`.
    ///
    /// **Deliberately NOT on the [[Idl]] value**, where Phase 116 put the vocabulary
    /// TOKENS ([[HardenPolicy]]). Two reasons, and they are different reasons. The
    /// allowlist is deployment trust state — module ids and content hashes — and the
    /// `Idl` is projected into `idl.json`, so carrying it there would publish it as if
    /// it were vocabulary. And the field sets are a security floor whose empty value is
    /// silent: a vocabulary migrating onto a declared policy by writing the default
    /// would stop sanitising and nothing would say so, which is the Phase 96 lesson
    /// (a floor that fails open survives because the claim was prose, not a test).
    ///
    /// Renamed from `HardenPolicy` at Phase 116, when that name was taken by the
    /// vocabulary tokens it is passed beside.
    type Policy =
        { Allowlist: AllowEntry list
          UrlFields: Set<string * string>
          MarkdownFields: Set<string * string> }

    /// The gate decision for a node of the gated kind.
    type CustomGate =
        | Allowed
        | InertPlaceholder of reason: string

    let private fieldOf (name: string) (fields: (string * IdlValue) list) : IdlValue option =
        fields |> List.tryPick (fun (k, v) -> if k = name then Some v else None)

    /// Gate a gated-kind node's fields. Unhashed → inert; hashed-but-not-allowlisted
    /// → inert; allowlisted + hash matches → live; allowlisted + hash MISMATCH →
    /// inert under `StrictReplay` / `Enforced`, live (advisory) under
    /// `AdvisoryWarning`.
    let internal gateCustom (allowlist: AllowEntry list) (fields: (string * IdlValue) list) : CustomGate =
        let str name =
            match fieldOf name fields with
            | Some(VStr s) -> s
            | _ -> ""

        let moduleId = str "moduleId"
        let componentId = str "componentId"

        match fieldOf "contentHash" fields with
        | Some(VRecord hfields) ->
            let hget name =
                match fieldOf name hfields with
                | Some(VStr s) -> s
                | Some(VEnum s) -> s
                | _ -> ""

            let hash = hget "hash"
            let strictness = hget "strictness"

            match
                allowlist
                |> List.tryFind (fun e -> e.ModuleId = moduleId && e.ComponentId = componentId)
            with
            | None -> InertPlaceholder "not in codegen allowlist"
            | Some e when e.Hash = hash -> Allowed
            | Some _ when strictness = "AdvisoryWarning" -> Allowed
            | Some _ -> InertPlaceholder "content-hash mismatch"
        | _ -> InertPlaceholder "unhashed Custom"

    /// The inert labelled placeholder a gated-out node becomes — a benign node of
    /// the vocabulary's declared placeholder kind (renders text, never a live call),
    /// preserving the node id.
    let private inertPlaceholder
        (tokens: HardenPolicy)
        (id: string)
        (moduleId: string)
        (componentId: string)
        (reason: string)
        : IdlValue =
        let label = sprintf "[inert placeholder: %s/%s — %s]" moduleId componentId reason

        VNode(
            id,
            tokens.PlaceholderKind,
            [ tokens.PlaceholderField, VUnion(tokens.TextLiteralCase, [ tokens.TextLiteralField, VStr label ]) ]
        )

    /// Sanitise a URL field value: the declared literal case of a binding-shaped union
    /// is routed through `Sanitize.sanitizeUrlOrBlank`; the union's other cases (a
    /// by-name reference, a host-resolved query, …) carry no literal URL and pass
    /// through.
    let private sanitiseUrlValue (tokens: HardenPolicy) (v: IdlValue) : IdlValue =
        match v with
        | VUnion(case, [ (field, VStr s) ]) when case = tokens.ValueLiteralCase && field = tokens.ValueLiteralField ->
            VUnion(case, [ field, VStr(Sanitize.sanitizeUrlOrBlank s) ])
        | other -> other

    /// Scrub a markdown field value: the declared literal case of a text-shaped union
    /// is routed through `Sanitize.scrubMarkdown`; other cases pass through.
    let private scrubMarkdownValue (tokens: HardenPolicy) (v: IdlValue) : IdlValue =
        match v with
        | VUnion(case, [ (field, VStr s) ]) when case = tokens.TextLiteralCase && field = tokens.TextLiteralField ->
            VUnion(case, [ field, VStr(Sanitize.scrubMarkdown s) ])
        | other -> other

    /// Harden an authored `IdlValue` for the codegen boundary: gate every node of the
    /// vocabulary's GATED kind to inert-by-default, and sanitise every declared URL /
    /// markdown field, recursively over the whole tree. The result scaffolds / encodes
    /// to inert-by-construction, sanitised output (Phase 321 tasks 2 + 3).
    ///
    /// Takes the vocabulary because the tokens it addresses by name — which kind is
    /// gated, what the placeholder is made of, which case carries a literal — are the
    /// vocabulary's ([[HardenPolicy]], Phase 116), while `policy` carries the caller's
    /// trust decisions. The two are separate arguments because they have separate
    /// owners, and only the first belongs in `idl.json`.
    let harden (idl: Idl) (policy: Policy) (v: IdlValue) : IdlValue =
        let tokens = idl.Harden

        let rec go (v: IdlValue) : IdlValue =
            match v with
            | VNode(id, kindTag, fields) when kindTag = tokens.GatedKind ->
                match gateCustom policy.Allowlist fields with
                | Allowed -> VNode(id, kindTag, fields |> List.map (fun (n, fv) -> n, go fv))
                | InertPlaceholder reason ->
                    let str name =
                        match fieldOf name fields with
                        | Some(VStr s) -> s
                        | _ -> ""

                    inertPlaceholder tokens id (str "moduleId") (str "componentId") reason
            | VNode(id, kindTag, fields) ->
                let hardenField (fieldName: string) (fv: IdlValue) : IdlValue =
                    let sanitised =
                        if Set.contains (kindTag, fieldName) policy.UrlFields then
                            sanitiseUrlValue tokens fv
                        elif Set.contains (kindTag, fieldName) policy.MarkdownFields then
                            scrubMarkdownValue tokens fv
                        else
                            fv

                    go sanitised

                VNode(id, kindTag, fields |> List.map (fun (n, fv) -> n, hardenField n fv))
            // Phase 698 — an enveloped node hardens as its bare form does, plus its
            // envelope values. It DELEGATES to the arms above rather than repeating
            // them: a second copy of the gate here is exactly how an enveloped node of
            // the gated kind would quietly stop being gated. When the gate replaces the
            // node with the inert placeholder the envelope goes with it — the
            // placeholder is a fresh inert node, not a re-dressed version of the one
            // that was refused.
            | VNodeEnv(id, envelope, kindTag, fields) ->
                let env = envelope |> List.map (fun (n, fv) -> n, go fv)

                match go (VNode(id, kindTag, fields)) with
                | VNode(hid, hTag, hFields) when hTag = kindTag -> VNodeEnv(hid, env, hTag, hFields)
                | replaced -> replaced
            | VList xs -> VList(xs |> List.map go)
            | VUnion(tag, fields) -> VUnion(tag, fields |> List.map (fun (n, fv) -> n, go fv))
            | VRecord fields -> VRecord(fields |> List.map (fun (n, fv) -> n, go fv))
            | VMap entries -> VMap(entries |> List.map (fun (k, fv) -> k, go fv))
            | other -> other

        go v

    /// Harden then scaffold an authored node to F# source (the codegen boundary
    /// end to end): the emitted source constructs an inert-by-construction,
    /// sanitised tree, prefixed with the Phase 321 provenance stamp. `wireHash` /
    /// `actor` feed the stamp.
    let scaffoldFSharp
        (policy: Policy)
        (idl: Idl)
        (wireHash: string)
        (actor: string)
        (v: IdlValue)
        : Result<string, string> =
        Gen.fsharpValue idl TNode (harden idl policy v)
        |> Result.map (fun body -> Gen.provenanceHeader "//" wireHash actor + "\n" + body)
