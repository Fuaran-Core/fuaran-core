namespace Fuaran.Core.Idl.Emit

open Fuaran.Core
open Fuaran.Core.Idl
open Fuaran.Core.Idl.Emit.Core
open Fuaran.Core.Idl.Emit.Annotations

/// Phase 293 — the ONE F# type emitter: fields, union cases, the wire-equality members, the
/// `RequireQualifiedAccess` enums and the recursion group every other declaration joins.
module internal FSharpTypes =

    let fsField (msg: Set<string>) (f: IdlField) : Result<string, CodegenError> =
        let fsType = fsTypeIn msg

        let tyR =
            match f.Opt with
            | Optional -> fsType f.Type |> Result.map (fun s -> s + " option")
            // OmitDefault fields always carry a value (the default is restored on
            // absence at decode) — a non-option field, like Required. HostOnly takes
            // the declared type verbatim: its `TFn` signature already says whether it
            // is an option (`Motion option`) or a bare value (`'Msg`).
            | Required
            | HostOnly
            | OmitDefault _ -> fsType f.Type

        // Phase 113 — a record field's attribute sits on its own line above it.
        let prefix =
            (annotationDocLines "      " f.Annotations
             @ (obsoleteAttr f.Annotations
                |> Option.map (fun a -> "      " + a)
                |> Option.toList))
            |> List.map (fun l -> l + "\n")
            |> String.concat ""

        tyR |> Result.map (fun ty -> prefix + sprintf "      %s: %s" (pascal f.Name) ty)

    /// Phase 119 — an enum case takes its annotations exactly where a UNION case takes
    /// them: the doc block above the bar, the single attribute INLINE after it, which is
    /// where F# accepts an attribute on a DU case. An unannotated enum emits the line it
    /// always did.
    let enumCaseDecl (e: IdlEnum) (case: string) =
        let a = e.AnnotationsOf case

        let docs =
            annotationDocLines "    " a |> List.map (fun l -> l + "\n") |> String.concat ""

        let attr =
            match obsoleteAttr a with
            | Some x -> x + " "
            | None -> ""

        docs + "    | " + attr + case

    let unionCaseDecl (msg: Set<string>) (c: IdlUnionCase) : Result<string, CodegenError> =
        let fsType = fsTypeIn msg

        let fieldDecl (f: IdlField) =
            let tyR =
                match f.Opt with
                | Optional -> fsType f.Type |> Result.map (fun s -> s + " option")
                | Required
                | HostOnly
                | OmitDefault _ -> fsType f.Type

            tyR |> Result.map (sprintf "%s: %s" (ident f.Name))

        c.Fields
        |> List.map fieldDecl
        |> concatR " * "
        |> Result.map (fun fields ->
            let body =
                if fields = "" then
                    c.Tag
                else
                    sprintf "%s of %s" c.Tag fields

            // Phase 113 — a union case's attribute sits INLINE after the bar
            // (`| [<Obsolete(...)>] Tag of ...`), which is where F# accepts it; the doc
            // block sits above the bar, like any other.
            let docs =
                annotationDocLines "    " c.Annotations
                |> List.map (fun l -> l + "\n")
                |> String.concat ""

            let attr =
                match obsoleteAttr c.Annotations with
                | Some a -> a + " "
                | None -> ""

            docs + "    | " + attr + body)

    /// Emit F# type declarations (enums, value-unions, per-kind spec records) from the IDL.
    ///
    /// **Phase 195 — this returns a `Result` where it used to return a bare `string`.** The
    /// type emitter can meet an IDL construct it does not emit (an op-vocabulary slot in a
    /// field type), and answering that by throwing made the one leg of the generator
    /// whose channel was a plain string the one leg whose refusal was an exception. It is the
    /// same [[CodegenError]] every other emitter already returned; the change is BREAKING for
    /// a caller that consumed the string directly, and `Result.defaultWith` over
    /// [[CodegenError.describe]] is the one-line adaptation for a caller that would rather
    /// keep failing loudly.
    /// Phase 252 — which generated RECORD-SHAPED declarations take custom WIRE equality:
    /// keys `"R:<record>"`, `"S:<kind tag>"` (a kind's spec record) and `"Node"`.
    ///
    /// A [[Optionality.HostOnly]] field is a closure, so the record holding it has no
    /// structural equality, and neither does anything that holds THAT — the generated `Node`
    /// included, which is what every tree-algebra conformance family compares. A host-only
    /// field is by definition invisible on the wire, so the record's wire-observable identity
    /// is its OTHER fields, and equality over them is exactly what the wire can tell apart.
    /// That is what these declarations get (DECISIONS, Phase 252).
    ///
    /// A declaration qualifies when it holds a host-only CLOSURE (a host-only field whose
    /// declared type is a function; a host-only data field compares already), is not generic in `'Msg`
    /// (its equality would need a constraint the generated layer cannot state), and every
    /// one of its wire fields is itself equality-capable — scalars, enums, JSON, the erased
    /// `unit` sentinels, and records / unions / nodes built of those or of a declaration that
    /// qualifies. A wire-visible closure (`TFn`) or a hosted host type (whose equality the
    /// IDL cannot see) disqualifies the declaration rather than emitting source that does not
    /// compile; such a vocabulary keeps the shape it had. The set is a fixpoint, because
    /// qualifying is mutually dependent across a recursive type group.
    let wireEqualityKeys (idl: Idl) (msg: Set<string>) (kinds: IdlKind list) (projected: Set<string>) : Set<string> =
        let fieldsOf (key: string) : IdlField list option =
            if key = "Node" then
                Some idl.NodeFields
            elif key.StartsWith "R:" then
                idl.Records
                |> List.tryFind (fun r -> r.Name = key.Substring 2)
                |> Option.map _.Fields
            // A PROJECTED kind's record is verbatim host source, so nothing here can say what it
            // compares — it neither qualifies nor counts as capable.
            elif key.StartsWith "S:" && not (projected.Contains(key.Substring 2)) then
                kinds |> List.tryFind (fun k -> k.Tag = key.Substring 2) |> Option.map _.Fields
            else
                None

        // A host-only field whose declared host type is a FUNCTION — the one kind of host-only
        // field that removes structural equality. A host-only DATA field (`Motion option`)
        // compares structurally already, and a declaration holding only such fields keeps the
        // equality it has.
        let hostOnlyClosure (f: IdlField) =
            f.Opt = HostOnly
            && (match f.Type with
                | TFn s -> s.FSharp.Contains "->"
                | _ -> false)

        let generic (key: string) =
            if key = "Node" then msg.Contains "Node"
            elif key.StartsWith "R:" then msg.Contains(key.Substring 2)
            else msg.Contains(key.Substring 2 + "Spec")

        let candidates =
            [ "Node"
              yield! idl.Records |> List.map (fun r -> "R:" + r.Name)
              yield! kinds |> List.map (fun k -> "S:" + k.Tag) ]
            |> List.filter (fun key ->
                not (generic key)
                && (fieldsOf key |> Option.exists (List.exists hostOnlyClosure)))
            |> Set.ofList

        let rec capable (granted: Set<string>) (visiting: Set<string>) (t: IdlType) : bool =
            match t with
            | TStr
            | TInt
            | TBool
            | TFloat
            | TEnum _
            | TJson
            | TClosure
            | TOpaque
            | TVar _ -> true
            | TList inner
            | TMap inner -> capable granted visiting inner
            | TRecord n -> declCapable granted visiting ("R:" + n)
            | TNode -> declCapable granted visiting "Node"
            | TUnion(n, args) ->
                args |> List.forall (capable granted visiting)
                && (match IdlLookup.tryUnion idl n with
                    | Some u when not (msg.Contains n) && not (Set.contains ("U:" + n) visiting) ->
                        u.Cases
                        |> List.forall (fun c ->
                            c.Fields
                            |> List.forall (fun f ->
                                not (hostOnlyClosure f)
                                && (f.Opt = HostOnly || capable granted (Set.add ("U:" + n) visiting) f.Type)))
                    | Some u when Set.contains ("U:" + n) visiting -> true
                    | _ -> false)
            | TFn _
            | THosted _
            | TKind
            | TOp -> false

        and declCapable (granted: Set<string>) (visiting: Set<string>) (key: string) : bool =
            if Set.contains key granted || Set.contains key visiting then
                true
            else
                let visiting = Set.add key visiting

                let own =
                    match fieldsOf key with
                    | Some fs ->
                        fs
                        |> List.forall (fun f ->
                            not (hostOnlyClosure f) && (f.Opt = HostOnly || capable granted visiting f.Type))
                    | None -> false

                // The node's equality also reaches every kind it can carry.
                own
                && (key <> "Node"
                    || kinds |> List.forall (fun k -> declCapable granted visiting ("S:" + k.Tag)))

        let wireFieldsCapable (granted: Set<string>) (key: string) =
            let own =
                fieldsOf key
                |> Option.defaultValue []
                |> List.filter (fun f -> f.Opt <> HostOnly)
                |> List.forall (fun f -> capable granted (Set.singleton key) f.Type)

            own
            && (key <> "Node"
                || kinds
                   |> List.forall (fun k -> declCapable granted (Set.singleton key) ("S:" + k.Tag)))

        let rec fixpoint (granted: Set<string>) =
            let next = granted |> Set.filter (wireFieldsCapable granted)
            if next = granted then granted else fixpoint next

        fixpoint candidates

    /// Phase 252 — the custom-equality members for a qualifying declaration (see
    /// [[wireEqualityKeys]]): `Equals` and `GetHashCode` over its WIRE fields, in declared
    /// order. `typeName` is the generated type, `wireFields` the fields' F# names.
    let wireEqualityMembers (typeName: string) (wireFields: string list) : string =
        let equals =
            match wireFields with
            | [] -> "true"
            | fs -> fs |> List.map (fun n -> sprintf "this.%s = o.%s" n n) |> String.concat " && "

        let hashed =
            match wireFields with
            | [] -> "0"
            | [ n ] -> sprintf "hash this.%s" n
            | fs -> "hash (" + (fs |> List.map (fun n -> "this." + n) |> String.concat ", ") + ")"

        sprintf
            "\n    // Phase 252 — wire equality: a host-only field is not on the wire, so it takes no part.\n    override this.Equals(other: obj) =\n        match other with\n        | :? %s as o -> %s\n        | _ -> false\n\n    override this.GetHashCode() = %s"
            typeName
            equals
            hashed

    /// A closed string set as a `[<RequireQualifiedAccess>]` union, with its declared docs.
    let rqaEnum (doc: string -> string -> string) (e: IdlEnum) =
        // Phase 945 — declared docs on the enum type and its cases.
        // Phase 119 — a case's declared ANNOTATIONS follow, in the same two places a
        // union case takes them: doc lines above the bar, the single attribute inline
        // after it. An unannotated enum emits exactly the lines it always did.
        let cases =
            e.Cases
            |> List.map (fun c -> doc ("case:" + e.Name + "." + c) "    " + enumCaseDecl e c)
            |> String.concat "\n"

        doc ("type:" + e.Name) ""
        + "[<RequireQualifiedAccess>]\n"
        + sprintf "type %s =\n%s" e.Name cases

    /// Phase 293 — THE ONE TYPE EMITTER. Value-unions, non-discriminated records, per-kind specs,
    /// `NodeKind` and `Node` as one `type … and …` recursion group, with each member's support doc,
    /// authored annotations, wire-equality members and (for a projected kind) the projection's
    /// record body. `fsharpModuleWith` renders it inside the module; `fsharpTypes` is its
    /// projection with no declared support. `doc` / `docOpt` are the support-doc lookups.
    let typeGroup
        (doc: string -> string -> string)
        (docOpt: string -> string option)
        (projections: Map<string, Projection>)
        (typeSplice: string option)
        (idl: Idl)
        (kinds: IdlKind list)
        (unions: IdlUnion list)
        (records: IdlRecord list)
        : Result<string, CodegenError> =
        let msg = msgCarrying idl
        let nodeArgs = declParams msg "Node" []
        let kindArgs = declParams msg "NodeKind" []

        let unionBody (u: IdlUnion) =
            u.Cases
            |> List.map (fun c ->
                unionCaseDecl msg c
                |> Result.map (fun d -> doc ("case:" + u.Name + "." + c.Tag) "    " + d))
            |> concatR "\n"
            |> Result.map (fun cases ->
                docOpt ("type:" + u.Name),
                [ "[<RequireQualifiedAccess>]" ],
                sprintf "%s%s =\n%s" u.Name (declParams msg u.Name u.Params) cases)

        // Phase 945 — field docs land above the field line, at field indent.
        let fieldDecls (owner: string) (fields: IdlField list) =
            fields
            |> List.map (fun f ->
                fsField msg f
                |> Result.map (fun d -> doc ("field:" + owner + "." + pascal f.Name) "      " + d))
            |> concatR "\n"

        // Phase 252 — the declarations that take wire equality, and the attribute + members
        // each gets. Empty for every vocabulary without a host-only field.
        let wireEq =
            wireEqualityKeys idl msg kinds (projections |> Map.toList |> List.map fst |> Set.ofList)

        let eqAttr key =
            if Set.contains key wireEq then
                [ "[<CustomEquality; NoComparison>]" ]
            else
                []

        let eqMembers key typeName (fields: IdlField list) =
            if Set.contains key wireEq then
                wireEqualityMembers
                    typeName
                    (fields
                     |> List.filter (fun f -> f.Opt <> HostOnly)
                     |> List.map (fun f -> pascal f.Name))
            else
                ""

        // Phase 303 — a declaration with NO fields has no record form (F# refuses `{ }`, FS3863),
        // so it is the single-case marker type `R = | R`; [[Core.recordLit]] spells its value.
        let declBody (typeName: string) (fields: IdlField list) (decls: string) =
            if List.isEmpty fields then
                sprintf "%s%s =\n    | %s" typeName (declParams msg typeName []) typeName
            else
                sprintf "%s%s =\n    {\n%s\n    }" typeName (declParams msg typeName []) decls

        let recordBody (r: IdlRecord) =
            fieldDecls r.Name r.Fields
            |> Result.map (fun fields ->
                docOpt ("type:" + r.Name),
                eqAttr ("R:" + r.Name),
                declBody r.Name r.Fields fields + eqMembers ("R:" + r.Name) r.Name r.Fields)

        let specBody (k: IdlKind) =
            // Phase 119 — the kind's own declared annotations: the doc block joins
            // the category comment and any Phase-945 declared doc above the
            // declaration, and the single `Obsolete` joins the attribute list, which
            // is what puts it in the position an `and`-joined member needs.
            // Phase 293 — the support doc ahead of the authored annotations: `memberDocLines`
            // is the one statement of that precedence.
            let comment =
                (fsCategoryComment k.Category)
                :: memberDocLines "" (docOpt ("type:" + k.Tag + "Spec")) k.Annotations
                |> String.concat "\n"
                |> Some

            let attrs = obsoleteAttr k.Annotations |> Option.toList

            // Phase 945 — a projected kind's record body is the projection's, verbatim.
            match projections.TryFind k.Tag with
            | Some proj -> Ok(comment, attrs, proj.SpecDecl)
            | None ->
                fieldDecls (k.Tag + "Spec") k.Fields
                |> Result.map (fun fields ->
                    comment,
                    attrs @ eqAttr ("S:" + k.Tag),
                    declBody (k.Tag + "Spec") k.Fields fields
                    + eqMembers ("S:" + k.Tag) (k.Tag + "Spec") k.Fields)

        let nodeKindBody =
            None,
            [ "[<RequireQualifiedAccess>]" ],
            "NodeKind"
            + declParams msg "NodeKind" []
            + " =\n"
            + (kinds
               |> List.map (fun k -> sprintf "    | %s of %sSpec%s" k.Tag k.Tag (declParams msg (k.Tag + "Spec") []))
               |> String.concat "\n")

        // Phase 690 — `id` + `kind` + the declared envelope. An IDL declaring no
        // envelope keeps the original one-liner, so nothing about it changes.
        let nodeBody =
            if List.isEmpty idl.NodeFields then
                Ok(None, [], sprintf "Node%s = { Id: string; Kind: NodeKind%s }" nodeArgs kindArgs)
            else
                idl.NodeFields
                |> List.map (fsField msg)
                |> sequenceR
                |> Result.map (fun envelope ->
                    let fields =
                        "      Id: string" :: sprintf "      Kind: NodeKind%s" kindArgs :: envelope

                    // Phase 252 — a host-only ENVELOPE member takes the node's equality
                    // away exactly as a spec's does; the node then compares id, kind and
                    // its wire envelope.
                    let members =
                        if Set.contains "Node" wireEq then
                            wireEqualityMembers
                                "Node"
                                ("Id"
                                 :: "Kind"
                                 :: (idl.NodeFields
                                     |> List.filter (fun f -> f.Opt <> HostOnly)
                                     |> List.map (fun f -> pascal f.Name)))
                        else
                            ""

                    None,
                    eqAttr "Node",
                    sprintf "Node%s =\n    {\n%s\n    }" nodeArgs (String.concat "\n" fields)
                    + members)

        // (comment, attributes, keyword-less body). The first member leads with `type`
        // and carries its attributes on their own preceding lines; the rest are
        // `and`-joined and carry theirs inline after the `and`, which is where F#
        // accepts an attribute on a member of a recursive type group.
        //
        // A LIST since Phase 119 rather than the `requiresQualifiedAccess` flag it
        // was: a kind can now earn an `Obsolete` beside — or instead of — the
        // `RequireQualifiedAccess` the unions carry, and a boolean cannot say that.
        // `[]` and `[ "[<RequireQualifiedAccess>]" ]` reproduce the two shapes the
        // flag had, so every unannotated vocabulary's emission is byte-identical.
        let membersR =
            (unions |> List.map unionBody)
            @ (records |> List.map recordBody)
            @ (kinds |> List.map specBody)
            @ [ Ok nodeKindBody; nodeBody ]
            |> sequenceR

        let render i (comment: string option, attrs: string list, body: string) =
            let commentPrefix =
                match comment with
                | Some c -> c + "\n"
                | None -> ""

            let keyword =
                if i = 0 then
                    (attrs |> List.map (fun a -> a + "\n") |> String.concat "") + "type"
                else
                    "and" + (attrs |> List.map (fun a -> " " + a) |> String.concat "")

            commentPrefix + keyword + " " + body

        membersR
        |> Result.map (fun members ->
            let rendered = members |> List.mapi render |> String.concat "\n\n"

            // Phase 945 — verbatim members appended to the SAME type-recursion group
            // (`and`-joined), so a spliced type may reference generated types freely.
            match typeSplice with
            | Some t -> rendered + "\n\n" + t
            | None -> rendered)

    /// The F# type declarations for a whole vocabulary — every enum, union, record and kind,
    /// plus `NodeKind` and `Node` — as the ONE type emitter renders them with no declared
    /// support (Phase 293). Before this phase a second, older emitter rendered the same
    /// declarations differently (no `RequireQualifiedAccess`, no records, no `Node`, no
    /// recursion group); there is one now, and this is its projection.
    let fsharpTypes (idl: Idl) : Result<string, CodegenError> =
        let noDoc (_: string) (_: string) = ""
        let noDocOpt (_: string) : string option = None
        let enums = idl.Enums |> List.map (rqaEnum noDoc) |> String.concat "\n\n"

        typeGroup noDoc noDocOpt Map.empty None idl idl.Kinds idl.Unions idl.Records
        |> Result.map (fun group -> (if enums = "" then group else enums + "\n\n" + group) |> normalizeEol)
