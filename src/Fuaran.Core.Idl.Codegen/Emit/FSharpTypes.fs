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
    /// Phase 252 — the custom-equality members for a qualifying declaration (see
    /// `Decl.EqualityCapable`): `Equals` and `GetHashCode` over its WIRE fields, in declared
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

        // Phase 542 — the enum's OWN annotations, after the support doc (the
        // [[memberDocLines]] precedence) and ahead of the attributes; empty for an enum
        // that says nothing, so its emission is the lines it always was.
        let annotated =
            annotationDocLines "" e.Annotations
            @ (obsoleteAttr e.Annotations |> Option.toList)
            |> List.map (fun l -> l + "\n")
            |> String.concat ""

        doc ("type:" + e.Name) ""
        + annotated
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
        (typeSplice: string option)
        (decl: Decl)
        : Result<string, CodegenError> =
        let idl, kinds, unions, records = decl.Idl, decl.Kinds, decl.Unions, decl.Records
        let projections = decl.Projections
        let msg = decl.Msg
        let nodeArgs = declParams msg "Node" []
        let kindArgs = declParams msg "NodeKind" []

        // Phase 542 — a union's or record's comment slot: its support doc, then its OWN
        // annotation lines ([[memberDocLines]]), or `None` when it has neither — so an
        // unannotated declaration's slot is exactly the support lookup it always was.
        let typeComment (name: string) (a: Annotations) =
            match memberDocLines "" (docOpt ("type:" + name)) a with
            | [] -> None
            | lines -> Some(String.concat "\n" lines)

        let unionBody (u: IdlUnion) =
            u.Cases
            |> List.map (fun c ->
                unionCaseDecl msg c
                |> Result.map (fun d -> doc ("case:" + u.Name + "." + c.Tag) "    " + d))
            |> concatR "\n"
            |> Result.map (fun cases ->
                typeComment u.Name u.Annotations,
                "[<RequireQualifiedAccess>]" :: (obsoleteAttr u.Annotations |> Option.toList),
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
        let wireEq = decl.EqualityCapable

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
                typeComment r.Name r.Annotations,
                (obsoleteAttr r.Annotations |> Option.toList) @ eqAttr ("R:" + r.Name),
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
    let fsharpTypes (decl: Decl) : Result<string, CodegenError> =
        let noDoc (_: string) (_: string) = ""
        let noDocOpt (_: string) : string option = None
        let enums = decl.Enums |> List.map (rqaEnum noDoc) |> String.concat "\n\n"

        typeGroup noDoc noDocOpt None decl
        |> Result.map (fun group -> (if enums = "" then group else enums + "\n\n" + group) |> normalizeEol)
