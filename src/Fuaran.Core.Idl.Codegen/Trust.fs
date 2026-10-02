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
    ///
    /// Phase 303 — a field declared as a plain `str` carries the URL itself, so its value is
    /// sanitised directly. [[checkHardenPolicy]] admits a URL entry only on those two shapes, so
    /// a bare `VStr` reaches this arm only from a `str`-typed field.
    let private sanitiseUrlValue (tokens: HardenPolicy) (v: IdlValue) : IdlValue =
        match v with
        | VUnion(case, [ (field, VStr s) ]) when case = tokens.ValueLiteralCase && field = tokens.ValueLiteralField ->
            VUnion(case, [ field, VStr(Sanitize.sanitizeUrlOrBlank s) ])
        | VStr s -> VStr(Sanitize.sanitizeUrlOrBlank s)
        | other -> other

    /// Scrub a markdown field value: the declared literal case of a text-shaped union
    /// is routed through `Sanitize.scrubMarkdown`; other cases pass through.
    let private scrubMarkdownValue (tokens: HardenPolicy) (v: IdlValue) : IdlValue =
        match v with
        | VUnion(case, [ (field, VStr s) ]) when case = tokens.TextLiteralCase && field = tokens.TextLiteralField ->
            VUnion(case, [ field, VStr(Sanitize.scrubMarkdown s) ])
        // Phase 303 — a `str`-typed markdown field is scrubbed directly, as a URL one is.
        | VStr s -> VStr(Sanitize.scrubMarkdown s)
        | other -> other

    /// The hardening TRANSFORM, without the declaration check — the body [[harden]]
    /// runs once [[checkHardenPolicy]] has passed.
    ///
    /// **Private, and that is the Phase 180 change.** It used to be the public
    /// `Trust.harden`: a total function that hardened through whatever tokens the
    /// vocabulary carried, including none, because `HardenPolicy.Default` guaranteed
    /// there were always some. With the default retired an undeclared [[GatedKind]] is
    /// the empty string, which matches no node tag — so an unchecked run over an
    /// undeclared policy gates NOTHING and says nothing about it, the Phase 96
    /// fail-open lesson in its purest form. Keeping it reachable would keep that
    /// failure one call site away; the check is not optional any more, so neither is
    /// the entry point that performs it.
    let private hardenUnchecked (idl: Idl) (policy: Policy) (v: IdlValue) : IdlValue =
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

    /// Phase 303 — the first URL or markdown entry of `policy` that the floor would leave
    /// UNSANITISED, as the refusal naming it; `None` when every entry is one the transform
    /// reaches. Before this check an entry was matched against node fields by name and its
    /// value sanitised only when it happened to be the declared literal case, so an entry
    /// naming no kind, no field, a record (whose nested `href` the transform never visits), a
    /// list or any other union was accepted and FAILED OPEN: the caller had declared the field
    /// sanitised and it was not.
    ///
    /// An entry is admitted on exactly the two shapes the transform rewrites: a `str` field
    /// (sanitised directly), or an instantiation of a union carrying the declared literal case
    /// — `ValueLiteralCase` for a URL entry, `TextLiteralCase` for a markdown one — whose one
    /// field is the declared literal field and resolves to `str`. Optionality does not matter:
    /// an absent value has nothing to sanitise. A `HostOnly` field never reaches the wire, so
    /// an entry naming one is refused as naming nothing the floor can reach.
    let private unsanitisableEntry (idl: Idl) (policy: Policy) : CodegenError option =
        let tokens = idl.Harden

        let literalUnion (caseTag: string) (fieldName: string) (name: string) (args: IdlType list) =
            match idl.Unions |> List.tryFind (fun u -> u.Name = name) with
            | None -> false
            | Some u ->
                match TypeParams.bind u args, u.Cases |> List.tryFind (fun c -> c.Tag = caseTag) with
                | Some subst, Some c ->
                    match c.Fields with
                    | [ single ] -> single.Name = fieldName && TypeParams.substitute subst single.Type = TStr
                    | _ -> false
                | _ -> false

        let check (what: string) (caseTag: string) (fieldName: string) (kindTag: string, field: string) =
            let refuse (why: string) =
                Some(
                    CodegenError.UnsupportedConstruct(
                        sprintf "the %s harden entry ('%s', '%s'): %s" what kindTag field why,
                        "a field a caller declares sanitised is never silently left unsanitised",
                        sprintf
                            "declare the entry on a 'str' field, or on a union carrying the '%s' case whose one field '%s' is a 'str'; or drop it"
                            caseTag
                            fieldName
                    )
                )

            match idl.Kinds |> List.tryFind (fun k -> k.Tag = kindTag) with
            | None -> refuse "the vocabulary declares no such kind"
            | Some k ->
                match k.Fields |> List.tryFind (fun f -> f.Name = field) with
                | None -> refuse "the kind declares no such field"
                | Some f when f.Opt = HostOnly -> refuse "the field is host-only and never reaches the wire"
                | Some f ->
                    match f.Type with
                    | TStr -> None
                    | TUnion(name, args) when literalUnion caseTag fieldName name args -> None
                    | other -> refuse (sprintf "the floor cannot sanitise a field of type %A" other)

        let urls =
            policy.UrlFields
            |> Seq.tryPick (check "URL" tokens.ValueLiteralCase tokens.ValueLiteralField)

        match urls with
        | Some r -> Some r
        | None ->
            policy.MarkdownFields
            |> Seq.tryPick (check "markdown" tokens.TextLiteralCase tokens.TextLiteralField)

    /// Refuse a vocabulary whose [[HardenPolicy]] leaves a member the run NEEDS
    /// undeclared (empty) — Phase 178's opt-in half, and since Phase 180 the gate
    /// [[harden]] runs unconditionally. It stays PUBLIC because the question it
    /// answers is separable: a caller assembling a vocabulary can ask whether the
    /// hardener would accept it before it has a tree to harden, and a codegen driver
    /// can report every such refusal at the point the vocabulary is loaded.
    ///
    /// **Static in `(idl, policy)`, not in the value.** Whether a member is needed is
    /// decided by the vocabulary and the caller's trust decisions, never by which nodes
    /// a particular tree happens to contain: the gate runs over every harden of a
    /// vocabulary that declares a gated kind, so the four members its inert placeholder
    /// is built from are needed whenever it does; the URL literal members are needed
    /// exactly when the caller declared a URL field to sanitise, and the text literal
    /// members whenever a markdown field is declared. A value-dependent answer would be worse than useless here — a
    /// tree with no `Custom` node today would pass, and the same vocabulary would refuse
    /// tomorrow on a document nobody changed.
    ///
    /// [[HardenPolicy.TransparentUnions]] is never refused: it is a list, and an empty
    /// one is the honest declaration of a vocabulary no case of which encodes bare —
    /// `ReferenceIdl` says exactly that, in a comment, on purpose.
    ///
    /// Reports the FIRST undeclared member in the record's own declaration order, so the
    /// refusal a caller sees does not depend on iteration order or on how many members
    /// are missing.
    ///
    /// **The gate is conditional on a declared gated kind (Phase 252).** A vocabulary
    /// with no foreign component has nothing to gate, and refusing it for not naming
    /// one shut it out of the checked path altogether. So an EMPTY [[GatedKind]] means
    /// "no gate": the placeholder members are not needed, and the run still sanitises
    /// every declared URL / markdown field under the tokens those need. What keeps that
    /// from being the Phase 96 fail-open is the structural check beside it: a vocabulary
    /// that leaves [[GatedKind]] empty while declaring a kind carrying the fields the
    /// gate reads (`moduleId` and `componentId`) is refused by name, because that is a
    /// foreign-component kind nobody declared as gated — decided from the vocabulary,
    /// never from the tree.
    ///
    /// **The caller's entries are checked too (Phase 303).** Once every needed member is
    /// declared, each `UrlFields` / `MarkdownFields` entry must name a declared kind's
    /// wire field whose type the floor rewrites — a `str`, or a union carrying the declared
    /// literal case — or the policy is refused as `UnsupportedConstruct`, naming the entry
    /// (see [[unsanitisableEntry]]). Sets are ordered, so the entry named is deterministic.
    let checkHardenPolicy (idl: Idl) (policy: Policy) : Result<unit, CodegenError> =
        let tokens = idl.Harden
        let gateDeclared = tokens.GatedKind <> ""

        let ungatedForeignKind =
            if gateDeclared then
                None
            else
                idl.Kinds
                |> List.tryFind (fun k ->
                    let names = k.Fields |> List.map (fun f -> f.Name) |> Set.ofList
                    names.Contains "moduleId" && names.Contains "componentId")

        let needed =
            (match ungatedForeignKind with
             | Some k ->
                 [ "GatedKind",
                   "",
                   sprintf
                       "the gate: kind '%s' carries 'moduleId' and 'componentId', the fields a foreign-component gate reads, and no gated kind is declared"
                       k.Tag ]
             | None -> [])
            @ (if gateDeclared then
                   [ "PlaceholderKind", tokens.PlaceholderKind, "the inert placeholder the gate mints"
                     "PlaceholderField", tokens.PlaceholderField, "the inert placeholder the gate mints"
                     "TextLiteralCase", tokens.TextLiteralCase, "the inert placeholder the gate mints"
                     "TextLiteralField", tokens.TextLiteralField, "the inert placeholder the gate mints" ]
               elif Set.isEmpty policy.MarkdownFields then
                   []
               else
                   [ "TextLiteralCase", tokens.TextLiteralCase, "a declared markdown field"
                     "TextLiteralField", tokens.TextLiteralField, "a declared markdown field" ])
            @ (if Set.isEmpty policy.UrlFields then
                   []
               else
                   [ "ValueLiteralCase", tokens.ValueLiteralCase, "a declared URL field"
                     "ValueLiteralField", tokens.ValueLiteralField, "a declared URL field" ])

        match needed |> List.tryFind (fun (_, value, _) -> value = "") with
        | Some(name, _, need) -> Error(CodegenError.UndeclaredHardenToken(name, need))
        | None ->
            match unsanitisableEntry idl policy with
            | Some refusal -> Error refusal
            | None -> Ok()

    /// Harden an authored `IdlValue` for the codegen boundary: refuse a vocabulary that
    /// leaves a needed [[HardenPolicy]] member undeclared, then gate every node of the
    /// vocabulary's GATED kind to inert-by-default and sanitise every declared URL /
    /// markdown field, recursively over the whole tree. The result scaffolds / encodes
    /// to inert-by-construction, sanitised output (Phase 321 tasks 2 + 3).
    ///
    /// Takes the vocabulary because the tokens it addresses by name — which kind is
    /// gated, what the placeholder is made of, which case carries a literal — are the
    /// vocabulary's ([[HardenPolicy]], Phase 116), while `policy` carries the caller's
    /// trust decisions. The two are separate arguments because they have separate
    /// owners, and only the first belongs in `idl.json`.
    ///
    /// **This is the CHECKED path, and Phase 180 is what made it the only one.** Phase
    /// 178 shipped the check as [[hardenOrRefuse]] beside an unchecked `harden`,
    /// because `HardenPolicy.Default` meant every vocabulary declared every member
    /// whether it had said so or not, and a refusal no default could raise did not
    /// justify a breaking signature. Retiring the default removes that guarantee: an
    /// undeclared policy is now reachable by saying nothing, so the difference between
    /// the two entry points is the difference between a refusal and a silent
    /// fail-open, and the safe one has to be the one a caller reaches by default.
    ///
    /// BREAKING: the return type widened from `IdlValue` to `Result<_, CodegenError>`.
    /// A caller that has measured its vocabulary and wants the value adapts with
    /// `|> Result.mapError CodegenError.describe`, or handles the refusal.
    let harden (idl: Idl) (policy: Policy) (v: IdlValue) : Result<IdlValue, CodegenError> =
        checkHardenPolicy idl policy
        |> Result.map (fun () -> hardenUnchecked idl policy v)

    /// [[harden]], under the name Phase 178 shipped it as. Kept as an ALIAS rather than
    /// deleted: it is the spelling every caller written between 178 and 180 uses, and
    /// the two now mean the same thing, so removing it would break source for no gain
    /// beyond having one name. New code says `harden`.
    let hardenOrRefuse (idl: Idl) (policy: Policy) (v: IdlValue) : Result<IdlValue, CodegenError> = harden idl policy v

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
        // Phase 180 — the hardening step can now REFUSE, so the refusal is threaded
        // here rather than absorbed. `describe` is the adaptation, because this
        // function's error channel is prose and every other arm of it already is.
        harden idl policy v
        |> Result.mapError CodegenError.describe
        |> Result.bind (Gen.fsharpValue idl TNode >> Result.mapError CodegenError.describe)
        |> Result.map (fun body -> Gen.provenanceHeader "//" wireHash actor + "\n" + body)
