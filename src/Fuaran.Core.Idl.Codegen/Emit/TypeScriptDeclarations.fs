namespace Fuaran.Core.Idl.Emit

open Fuaran.Core
open Fuaran.Core.Idl
open Fuaran.Core.Idl.Emit.Core
open Fuaran.Core.Idl.Emit.Reach
open Fuaran.Core.Idl.Emit.Annotations
open Fuaran.Core.Idl.Emit.FSharpDefaults

/// The TypeScript backend's scaffold value emitter and its declarations (`TypeScript` until the Phase
/// 388 split along its banners).
module internal TypeScriptDeclarations =
    open TypeScriptCodec

    // -----------------------------------------------------------------------
    // Phase 252 — the TypeScript scaffold leg in the DECODER's shape, and the
    // module's type declarations.
    // -----------------------------------------------------------------------

    /// Phase 252 — a TypeScript value literal for an authored `IdlValue` of type `t`, in
    /// the SHAPE the generated decoder produces, which is the shape the generated encoder
    /// reads: every omit-at-default member is PRESENT, filled from its declared default
    /// as [[fsharpValue]] fills it; an absent optional and a host-only member are absent.
    ///
    /// BREAKING (Phase 252): it took the value alone (`typescriptValue v : string`), and a value
    /// alone cannot know a default, so a node it scaffolded left such a member `undefined` and
    /// the encoder then wrote `"quantity":undefined` (not JSON) or a default the wire omits.
    /// A caller passes the vocabulary and the slot's type (`TNode` for a node) and handles the
    /// [[CodegenError]] a value that does not fit its declaration now raises.
    ///
    /// Walks the IDL rather than the value, so a value that does not fit its declaration
    /// is refused (a [[CodegenError]]) rather than emitted. Respects the declared wire
    /// shape (discriminator key, node envelope) exactly as [[typescriptValueWith]] does.
    let typescriptValue (idl: Idl) (t: IdlType) (v: IdlValue) : Result<string, CodegenError> =
        let disc = tsDiscKey idl.Wire.Discriminator

        let mismatch (what: string) =
            Error(
                CodegenError.UnsupportedConstruct(
                    what,
                    "GP5: the scaffold leg names what it cannot construct rather than emitting a value the encoder mis-writes",
                    "author the value against the vocabulary's declaration (Encode.encode refuses the same value)"
                )
            )

        let objectOf (pairs: (string * string) list) =
            "{ "
            + (pairs |> List.map (fun (k, e) -> k + ": " + e) |> String.concat ", ")
            + " }"

        let rec go (subst: Map<string, IdlType>) (t: IdlType) (v: IdlValue) : Result<string, CodegenError> =
            match TypeParams.substitute subst t, v with
            | TStr, VStr s -> Ok(SourceLit.tsString s)
            | TInt, VInt i -> Ok(string i)
            | TBool, VBool b -> Ok(if b then "true" else "false")
            | TFloat, VFloat f -> Ok(invariantFloat f)
            | TFloat, VInt i -> Ok(string i)
            | TEnum n, VEnum wire ->
                match IdlLookup.tryEnum idl n with
                | Some e when List.contains wire e.WireCases -> Ok(SourceLit.tsString wire)
                | _ -> mismatch (sprintf "the wire string %A at enum '%s'" wire n)
            | TList inner, VList xs ->
                xs
                |> List.map (go subst inner)
                |> sequenceR
                |> Result.map (fun items -> "[" + String.concat ", " items + "]")
            | TMap vt, VMap entries ->
                entries
                |> List.map (fun (k, ev) -> go subst vt ev |> Result.map (fun e -> SourceLit.tsString k, e))
                |> sequenceR
                |> Result.map objectOf
            // A hosted slot that declares its wire form takes that type's decoded shape (a
            // record's omit-at-default members filled, as for any record); the JSON is read
            // through the interpreter's decoder, so a value outside the form is refused.
            | THosted { Wire = Some w }, VJson j ->
                match Decode.value idl w j with
                | Ok wv -> go subst w wv
                | Error m -> mismatch (sprintf "a hosted value outside its declared wire form (%s)" m)
            | (TJson | THosted _), VJson j -> Ok(Canon.render j)
            // The encoder never reads a sentinel slot; a PRESENT stand-in keeps an optional
            // one's presence test honest (see [[typescriptValueWith]]).
            | (TClosure | TFn _ | TOpaque), (VClosure | VOpaque) -> Ok "(() => undefined)"
            | TRecord n, VRecord authored ->
                match IdlLookup.tryRecord idl n with
                | None -> mismatch (sprintf "a value of the undeclared record '%s'" n)
                | Some r -> members subst ("record '" + n + "'") r.Fields authored |> Result.map objectOf
            | TUnion(n, args), VUnion(tag, authored) ->
                match IdlLookup.tryUnion idl n with
                | None -> mismatch (sprintf "a value of the undeclared union '%s'" n)
                | Some u when List.length u.Params <> List.length args ->
                    mismatch (sprintf "union '%s' applied to %d type arguments" n (List.length args))
                | Some u ->
                    match u.Cases |> List.tryFind (fun c -> c.Tag = tag) with
                    | None -> mismatch (sprintf "the case '%s', which union '%s' does not declare" tag n)
                    | Some c ->
                        let caseSubst = TypeParams.bind u args |> Option.defaultValue Map.empty

                        members caseSubst (sprintf "union case '%s.%s'" n tag) c.Fields authored
                        |> Result.map (fun ms -> objectOf ((disc, SourceLit.tsString tag) :: ms))
            | TNode, VNode(id, kindTag, fields) -> node id [] kindTag fields
            | TNode, VNodeEnv(id, envelope, kindTag, fields) -> node id envelope kindTag fields
            | _, VAbsent -> mismatch "an absent value (VAbsent) in a value position"
            | t', _ -> mismatch (sprintf "a value that does not match IDL type %A" t')

        /// The members of an object under its declared fields' presence rules.
        and members
            (subst: Map<string, IdlType>)
            (where: string)
            (declared: IdlField list)
            (authored: (string * IdlValue) list)
            : Result<(string * string) list, CodegenError> =
            declared
            |> List.map (fun f ->
                let value =
                    authored
                    |> List.tryPick (fun (n, av) -> if n = f.Name && av <> VAbsent then Some av else None)

                match f.Opt, value with
                // Never on the wire, and `undefined` in the decoder's shape.
                | HostOnly, _ -> Ok None
                | _, Some av -> go subst f.Type av |> Result.map (fun e -> Some(SourceLit.tsKey f.Name, e))
                | Optional, None -> Ok None
                | OmitDefault d, None -> go subst f.Type d |> Result.map (fun e -> Some(SourceLit.tsKey f.Name, e))
                | Required, None -> mismatch (sprintf "%s without its required field '%s'" where f.Name))
            |> sequenceR
            |> Result.map (List.choose id)

        and node (id: string) envelope (kindTag: string) fields : Result<string, CodegenError> =
            match IdlLookup.tryKind idl kindTag with
            | None -> mismatch (sprintf "a node of the undeclared kind '%s'" kindTag)
            | Some k ->
                match
                    members Map.empty ("kind '" + kindTag + "'") k.Fields fields,
                    members Map.empty "node envelope" idl.NodeFields envelope
                with
                | Error e, _
                | _, Error e -> Error e
                | Ok kindMembers, Ok envMembers ->
                    let tagged = (disc, SourceLit.tsString kindTag)

                    match idl.Wire.NodeEnvelope with
                    | NodeEnvelopeShape.NestedKind ->
                        Ok(
                            objectOf (
                                (("id", SourceLit.tsString id) :: envMembers)
                                @ [ "kind", objectOf (tagged :: kindMembers) ]
                            )
                        )
                    | NodeEnvelopeShape.FlatKind ->
                        Ok(objectOf ((tagged :: ("id", SourceLit.tsString id) :: envMembers) @ kindMembers))

        go Map.empty t v

    /// The name the declaration file gives a decode refusal (Phase 348).
    let internal refusalTypeName = "DecodeRefusal"

    /// Phase 348 — the decode refusal's declared shape: the object `decodeNode` answers with since
    /// Phase 337, `DecodeError.toJson`'s members — the code from the closed set (spelled as every
    /// host spells it, read off [[DecodeError.codes]] so a code added there is declared here), the
    /// path from the document root (a member key or an item index per step), what the position
    /// expected, and the sentence. One exported type, which every decode signature names.
    let private refusalDecl: string =
        "/** A decode refusal: what kind of fault, where (root-first steps: a member key or an item index), what the position admits, and a sentence. */\n"
        + "export type "
        + refusalTypeName
        + " = { code: "
        + (DecodeError.codes
           |> List.map (DecodeError.codeName >> SourceLit.tsString)
           |> String.concat " | ")
        + "; path: Array<string | number>; expected: string; message: string };"

    /// The declared TypeScript type of an IDL type, as the declaration file spells it: a declared
    /// enum, record or union by its own name, a node as `Node`, and a sentinel, JSON or undeclared
    /// hosted slot as `unknown`. Shared by [[typescriptDeclarations]] and the derived members'
    /// declarations (Phase 381), so a member's type is spelled exactly as the file's own types are.
    let rec internal tsDeclType (t: IdlType) : Result<string, CodegenError> =
        match t with
        | TStr -> Ok "string"
        | TInt
        | TFloat -> Ok "number"
        | TBool -> Ok "boolean"
        | TEnum n
        | TRecord n
        | TUnion(n, []) -> Ok n
        | TUnion(n, args) ->
            args
            |> List.map tsDeclType
            |> concatR ", "
            |> Result.map (fun a -> n + "<" + a + ">")
        | TVar v -> Ok v
        | TNode -> Ok "Node"
        | TList inner -> tsDeclType inner |> Result.map (fun s -> "Array<" + s + ">")
        | TMap vt -> tsDeclType vt |> Result.map (fun s -> "{ [key: string]: " + s + " }")
        // A hosted slot that declares its wire form IS that type on this side.
        | THosted { Wire = Some w } -> tsDeclType w
        | TJson
        | THosted _
        | TClosure
        | TFn _
        | TOpaque -> Ok "unknown"
        | TKind
        | TOp -> Error(opVocabularySlot "the TypeScript declaration backend" t)

    /// Phase 252 — TypeScript TYPE DECLARATIONS for the module [[typescriptModule]] emits
    /// over the same kinds: one declaration per enum, record, union and kind spec the
    /// module reaches, the `Node` type, and the signatures of `encodeNode` / `decodeNode`.
    /// Written beside the module (`generated.mjs` + `generated.d.mts`), it is what lets a
    /// TypeScript consumer's compiler catch a scaffolded value of the wrong shape — the
    /// module is untyped JavaScript, so nothing did.
    ///
    /// The types describe the DECODER's shape, which is the one the encoder reads: an
    /// omit-at-default member is present (not optional), an optional member is `?:`, a
    /// host-only member is `?:` (it is never on the wire), an enum is the union of its wire
    /// strings, and a sentinel, JSON or hosted slot is `unknown` (the TypeScript tier
    /// carries a hosted slot's JSON verbatim).
    ///
    /// Phase 348 — `decodeNode`'s refusal is declared as the object it is, the exported
    /// `DecodeRefusal` (it read `error: string` after Phase 337 made it an object), and a
    /// vocabulary type spelled like a name the file declares for itself (`Node`, `NodeKind`, a
    /// `<Kind>Spec`, `DecodeRefusal`) is refused as `UnsupportedConstruct` rather than declared
    /// twice.
    let typescriptDeclarations (idl: Idl) (kindTags: string list) : Result<string, CodegenError> =
        let kinds = kindTags |> List.choose (fun t -> IdlLookup.tryKind idl t)

        let enums, unions, records = referenced idl kinds
        let disc = tsDiscKey idl.Wire.Discriminator
        let tsType = tsDeclType

        let memberDecl (f: IdlField) : Result<string, CodegenError> =
            tsType f.Type
            |> Result.map (fun ty ->
                match f.Opt with
                | Optional
                | HostOnly -> SourceLit.tsKey f.Name + "?: " + ty
                | Required
                | OmitDefault _ -> SourceLit.tsKey f.Name + ": " + ty)

        let objectType (extra: string list) (fields: IdlField list) : Result<string, CodegenError> =
            fields
            |> List.map memberDecl
            |> sequenceR
            |> Result.map (fun ms ->
                match extra @ ms with
                | [] -> "{}"
                | all -> "{ " + String.concat "; " all + " }")

        let generic (ps: string list) =
            match ps with
            | [] -> ""
            | _ -> "<" + String.concat ", " ps + ">"

        let orNever (xs: string list) =
            match xs with
            | [] -> "never"
            | _ -> String.concat " | " xs

        let enumDecls =
            enums
            |> List.map (fun e ->
                Ok(
                    "export type "
                    + e.Name
                    + " = "
                    + orNever (e.WireCases |> List.map SourceLit.tsString)
                    + ";"
                ))

        let recordDecls =
            records
            |> List.map (fun r ->
                objectType [] r.Fields
                |> Result.map (fun o -> "export type " + r.Name + " = " + o + ";"))

        let unionDecls =
            unions
            |> List.map (fun u ->
                u.Cases
                |> List.map (fun c -> objectType [ disc + ": " + SourceLit.tsString c.Tag ] c.Fields)
                |> sequenceR
                |> Result.map (fun cases -> "export type " + u.Name + generic u.Params + " = " + orNever cases + ";"))

        let specDecls =
            kinds
            |> List.map (fun k ->
                objectType [ disc + ": " + SourceLit.tsString k.Tag ] k.Fields
                |> Result.map (fun o -> "export type " + k.Tag + "Spec = " + o + ";"))

        let envelope = objectType [ "id: string" ] idl.NodeFields
        let specs = kinds |> List.map (fun k -> k.Tag + "Spec")

        let nodeDecl =
            envelope
            |> Result.map (fun env ->
                match idl.Wire.NodeEnvelope with
                | NodeEnvelopeShape.NestedKind ->
                    "export type NodeKind = "
                    + orNever specs
                    + ";\n"
                    + "export type Node = "
                    + env.Substring(0, env.Length - 2)
                    + "; kind: NodeKind };"
                | NodeEnvelopeShape.FlatKind ->
                    "export type Node = "
                    + orNever (specs |> List.map (fun s -> "(" + s + " & " + env + ")"))
                    + ";")

        // Phase 348 — the names this file declares beside the vocabulary's own: a vocabulary type
        // spelled like one of them would be declared twice, which a consumer's compiler refuses
        // far from its cause. Refused here, naming it.
        let fixedNames =
            [ "Node"; refusalTypeName ]
            @ (match idl.Wire.NodeEnvelope with
               | NodeEnvelopeShape.NestedKind -> [ "NodeKind" ]
               | NodeEnvelopeShape.FlatKind -> [])
            @ specs

        let clash =
            (enums |> List.map _.Name)
            @ (unions |> List.map _.Name)
            @ (records |> List.map _.Name)
            |> List.tryFind (fun n -> List.contains n fixedNames)

        match clash with
        | Some name ->
            Error(
                CodegenError.UnsupportedConstruct(
                    sprintf "a vocabulary type named '%s' in the TypeScript declarations" name,
                    "the declaration file already declares that name for the node, a kind spec or the decode refusal",
                    "rename the type"
                )
            )
        | None ->
            [ [ Ok(
                    "// AUTO-GENERATED from the IDL by Fuaran.Core.Idl.Gen (Phase 252 — type declarations for the TS backend). Do not edit by hand."
                ) ]
              enumDecls
              recordDecls
              unionDecls
              specDecls
              [ nodeDecl ]
              [ Ok(
                    refusalDecl
                    + "\n\n"
                    + "export declare function encodeNode(n: Node): string;\n"
                    + "export declare function decodeNode(s: string): { ok: true; value: Node } | { ok: false; error: "
                    + refusalTypeName
                    + " };"
                ) ] ]
            |> List.concat
            |> concatR "\n\n"
            |> Result.map (fun s -> normalizeEol s + "\n")
