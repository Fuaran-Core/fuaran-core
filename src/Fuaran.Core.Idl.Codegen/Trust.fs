namespace Fuaran.Core.Idl

open System

/// The codegen trust boundary over the `IdlValue` tree (Phase 321 task 2 + the
/// task-3 wiring): gate every `Custom` against an allowlist + content-hash, and
/// run `Sanitize.*` over declared URL / markdown fields, producing a hardened
/// value that is inert-by-construction when scaffolded to host source.
module Trust =

    /// An allowlisted `Custom` component: which module/component may resolve live,
    /// and the content-hash it must carry.
    type AllowEntry =
        { ModuleId: string
          ComponentId: string
          Hash: string }

    /// Which fields carry values that must be sanitised at codegen time. A URL
    /// field is a `Binding<string>` whose `Static` value is a URL; a markdown
    /// field is a `TextSource` whose `Literal` text is markdown. Keyed by
    /// `(kindTag, fieldName)`.
    type HardenPolicy =
        { Allowlist: AllowEntry list
          UrlFields: Set<string * string>
          MarkdownFields: Set<string * string> }

    /// The default UI-tier policy: `Link.href` / `Image.src` are URLs;
    /// `Markdown.text` is markdown. Callers pass their own allowlist.
    let uiPolicy (allowlist: AllowEntry list) : HardenPolicy =
        { Allowlist = allowlist
          UrlFields = Set.ofList [ "Link", "href"; "Image", "src" ]
          MarkdownFields = Set.ofList [ "Markdown", "text" ] }

    /// The gate decision for a `Custom` node.
    type CustomGate =
        | Allowed
        | InertPlaceholder of reason: string

    let private fieldOf (name: string) (fields: (string * IdlValue) list) : IdlValue option =
        fields |> List.tryPick (fun (k, v) -> if k = name then Some v else None)

    /// Gate a `Custom` node's fields. Unhashed → inert; hashed-but-not-allowlisted
    /// → inert; allowlisted + hash matches → live; allowlisted + hash MISMATCH →
    /// inert under `StrictReplay` / `Enforced`, live (advisory) under
    /// `AdvisoryWarning`.
    let gateCustom (allowlist: AllowEntry list) (fields: (string * IdlValue) list) : CustomGate =
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

    /// The inert labelled placeholder a gated-out `Custom` becomes — a benign
    /// `Markdown` node (renders text, never a live call), preserving the node id.
    let private inertPlaceholder (id: string) (moduleId: string) (componentId: string) (reason: string) : IdlValue =
        let label = sprintf "[inert placeholder: %s/%s — %s]" moduleId componentId reason
        VNode(id, "Markdown", [ "text", VUnion("Literal", [ "text", VStr label ]) ])

    /// Sanitise a URL field value (a `Binding<string>`): a `Static` string literal
    /// is routed through `Sanitize.sanitizeUrlOrBlank`; other binding cases (Query
    /// / State / …) carry no literal URL and pass through.
    let private sanitiseUrlValue (v: IdlValue) : IdlValue =
        match v with
        | VUnion("Static", [ ("value", VStr s) ]) -> VUnion("Static", [ "value", VStr(Sanitize.sanitizeUrlOrBlank s) ])
        | other -> other

    /// Scrub a markdown field value (a `TextSource`): a `Literal` string is routed
    /// through `Sanitize.scrubMarkdown`; other cases pass through.
    let private scrubMarkdownValue (v: IdlValue) : IdlValue =
        match v with
        | VUnion("Literal", [ ("text", VStr s) ]) -> VUnion("Literal", [ "text", VStr(Sanitize.scrubMarkdown s) ])
        | other -> other

    /// Harden an authored `IdlValue` for the codegen boundary: gate every `Custom`
    /// node to inert-by-default, and sanitise every declared URL / markdown field,
    /// recursively over the whole tree. The result scaffolds / encodes to
    /// inert-by-construction, sanitised output (Phase 321 tasks 2 + 3).
    let rec harden (policy: HardenPolicy) (v: IdlValue) : IdlValue =
        match v with
        | VNode(id, "Custom", fields) ->
            match gateCustom policy.Allowlist fields with
            | Allowed -> VNode(id, "Custom", fields |> List.map (fun (n, fv) -> n, harden policy fv))
            | InertPlaceholder reason ->
                let str name =
                    match fieldOf name fields with
                    | Some(VStr s) -> s
                    | _ -> ""

                inertPlaceholder id (str "moduleId") (str "componentId") reason
        | VNode(id, kindTag, fields) ->
            let hardenField (fieldName: string) (fv: IdlValue) : IdlValue =
                let sanitised =
                    if Set.contains (kindTag, fieldName) policy.UrlFields then
                        sanitiseUrlValue fv
                    elif Set.contains (kindTag, fieldName) policy.MarkdownFields then
                        scrubMarkdownValue fv
                    else
                        fv

                harden policy sanitised

            VNode(id, kindTag, fields |> List.map (fun (n, fv) -> n, hardenField n fv))
        // Phase 698 — an enveloped node hardens as its bare form does, plus its
        // envelope values. It DELEGATES to the arms above rather than repeating
        // them: a second copy of the Custom gate here is exactly how an enveloped
        // `Custom` node would quietly stop being gated. When the gate replaces the
        // node with the inert placeholder the envelope goes with it — the
        // placeholder is a fresh inert node, not a re-dressed version of the one
        // that was refused.
        | VNodeEnv(id, envelope, kindTag, fields) ->
            let env = envelope |> List.map (fun (n, fv) -> n, harden policy fv)

            match harden policy (VNode(id, kindTag, fields)) with
            | VNode(hid, hTag, hFields) when hTag = kindTag -> VNodeEnv(hid, env, hTag, hFields)
            | replaced -> replaced
        | VList xs -> VList(xs |> List.map (harden policy))
        | VUnion(tag, fields) -> VUnion(tag, fields |> List.map (fun (n, fv) -> n, harden policy fv))
        | VRecord fields -> VRecord(fields |> List.map (fun (n, fv) -> n, harden policy fv))
        | VMap entries -> VMap(entries |> List.map (fun (k, fv) -> k, harden policy fv))
        | other -> other

    /// Harden then scaffold an authored node to F# source (the codegen boundary
    /// end to end): the emitted source constructs an inert-by-construction,
    /// sanitised tree, prefixed with the Phase 321 provenance stamp. `wireHash` /
    /// `actor` feed the stamp.
    let scaffoldFSharp
        (policy: HardenPolicy)
        (idl: Idl)
        (wireHash: string)
        (actor: string)
        (v: IdlValue)
        : Result<string, string> =
        Gen.fsharpValue idl TNode (harden policy v)
        |> Result.map (fun body -> Gen.provenanceHeader "//" wireHash actor + "\n" + body)
